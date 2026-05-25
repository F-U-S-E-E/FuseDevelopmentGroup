using FUSE.Data;
using UnityEngine;

namespace FUSE.API
{
    /// <summary>
    /// Pure-function planner that decides what mutations FUSE's scene-clone
    /// apply pipeline must perform on a target GameObject, given a parsed
    /// <see cref="FuseSceneClone"/> definition and whether the target path
    /// resolved to an existing GameObject. The planner exists to make those
    /// decisions testable in isolation — the actual mutation execution
    /// (calling <c>Transform.localPosition = ...</c>, <c>SetActive</c>,
    /// <c>Object.Destroy</c>, etc.) lives in
    /// <see cref="SceneCloneAPI.ApplyDefinition"/> because Unity refuses to
    /// instantiate a <c>GameObject</c> outside of a Player or the Editor.
    ///
    /// Every regression we've shipped in the mandela / scene-clone pipeline
    /// (the Bryson Freight House zeroing, the Ela freight house teleport,
    /// the duplicate-name empty placeholder pickup) was rooted in this
    /// decision logic. Pinning the planner with a unit-test suite — see
    /// <c>FUSE.Tests.API.FuseSceneCloneApplyPlannerTests</c> — closes the
    /// loop so any future change to <see cref="Compute"/> that breaks an
    /// invariant fails CI before it ships.
    /// </summary>
    internal static class FuseSceneCloneApplyPlanner
    {
        /// <summary>
        /// The set of mutations that should be applied to the target
        /// GameObject and its <see cref="Transform"/>. Each <c>Override*</c>
        /// nullable conveys "if set, force the live transform to this
        /// value; if null, leave the live value alone" — the same
        /// semantics the apply path executes. Side-effect flags
        /// (<see cref="DestroyExistingTarget"/>, <see cref="CloneFromSource"/>,
        /// etc.) tell the executor which Unity calls to make.
        /// </summary>
        public struct Plan
        {
            /// <summary>
            /// True when the definition supplied a non-empty Source —
            /// the apply path will destroy any existing target, walk
            /// the parent chain to ensure intermediates exist, resolve
            /// the source prefab, <see cref="Object.Instantiate{T}(T)"/>
            /// it under the resolved parent, rename it to the target
            /// leaf, reset its local transform to identity, strip
            /// unsupported runtime components, run the prefab
            /// sanitizer, and force renderers visible.
            /// </summary>
            public bool CloneFromSource;

            /// <summary>
            /// True when an existing target at the path was found AND
            /// the definition is cloning from a source (in which case
            /// the existing target gets destroyed before the new clone
            /// takes its place). Always false for enabled-only
            /// mandelas on a vanilla GameObject — those bind to the
            /// existing object and never destroy it.
            /// </summary>
            public bool DestroyExistingTarget;

            /// <summary>
            /// True when the apply path must walk the target path to
            /// ensure every intermediate transform exists before
            /// instantiating the clone. Only meaningful when
            /// <see cref="CloneFromSource"/> is true.
            /// </summary>
            public bool EnsureParentChainExists;

            /// <summary>
            /// True when the apply path must reset the newly
            /// instantiated clone's local transform to identity (zero
            /// position, identity rotation) immediately after
            /// instantiation so the explicit <see cref="OverrideLocalPosition"/>
            /// override (or its absence) is the authoritative setting.
            /// Only meaningful when <see cref="CloneFromSource"/> is true.
            /// </summary>
            public bool ZeroLocalTransformBeforeOverride;

            /// <summary>
            /// True when the apply path must run the FUSE prefab
            /// sanitizer (replace global object ids, disable track
            /// markers, clear cached identity fields, validate
            /// renderer presence) on the cloned subtree. Only
            /// meaningful when <see cref="CloneFromSource"/> is true —
            /// bound vanilla targets are never sanitised because
            /// doing so would mutate base-game state.
            /// </summary>
            public bool RunPrefabSanitizer;

            /// <summary>
            /// True when the apply path must strip components like
            /// KinematicCharacterController that don't make sense on
            /// scenery clones (they're physics-active and would
            /// disrupt the scene). Only meaningful when
            /// <see cref="CloneFromSource"/> is true.
            /// </summary>
            public bool StripUnsupportedRuntimeComponents;

            /// <summary>
            /// True when the apply path should walk the cloned subtree
            /// and force every renderer visible (clearing
            /// <c>forceRenderingOff</c>, setting <c>enabled</c>,
            /// forcing <c>LODGroup.ForceLOD(0)</c>). Only meaningful
            /// when <see cref="CloneFromSource"/> is true AND the
            /// definition does not explicitly disable the clone via
            /// <see cref="FuseSceneClone.Enabled"/>=<c>false</c>.
            /// </summary>
            public bool ForceRenderable;

            /// <summary>
            /// True when the apply path should run the post-bind
            /// sanitizer's validation pass (renderer presence,
            /// transform finiteness, marker registration). Skipped
            /// for entries the definition explicitly disables so we
            /// don't emit "no renderer components" warnings against
            /// targets the author specifically asked to hide.
            /// </summary>
            public bool RunPostBindValidation;

            /// <summary>
            /// When non-null, the apply path must set the target's
            /// <c>transform.localPosition</c> to this value. When
            /// null, the target's existing local position is
            /// preserved — this is the contract that an
            /// <c>{ enabled: true }</c> mandela on a vanilla
            /// GameObject must NOT teleport the base-game object.
            /// </summary>
            public Vector3? OverrideLocalPosition;

            /// <summary>Same contract as <see cref="OverrideLocalPosition"/>, for rotation (Euler angles).</summary>
            public Vector3? OverrideLocalRotation;

            /// <summary>Same contract as <see cref="OverrideLocalPosition"/>, for scale.</summary>
            public Vector3? OverrideLocalScale;

            /// <summary>
            /// When non-null, the apply path must call
            /// <c>GameObject.SetActive(value)</c>. When null, the
            /// target's existing active state is preserved.
            /// </summary>
            public bool? SetActiveState;
        }

        /// <summary>
        /// Compute the apply plan for a given scene-clone definition.
        /// </summary>
        /// <param name="definition">The parsed scene-clone definition.
        /// Caller is responsible for never passing <c>null</c>; if a
        /// caller does pass <c>null</c> the plan returned describes a
        /// no-op (no clone, no overrides, no SetActive).</param>
        /// <param name="existingTargetFound">Whether
        /// <see cref="SceneCloneAPI.ApplyDefinition"/>'s
        /// <see cref="FusePrefabResolver.ResolveScenePath"/> returned
        /// a non-null GameObject at <see cref="FuseSceneClone.TargetPath"/>.
        /// Only matters when the definition also has a Source — that's
        /// the case where the existing target gets destroyed before
        /// being replaced.</param>
        public static Plan Compute(FuseSceneClone definition, bool existingTargetFound)
        {
            if (definition == null)
            {
                return default;
            }

            // A definition is "cloned from source" iff it carries a
            // non-empty Source path. This single flag drives every
            // branch in the apply pipeline — the entire "instantiate /
            // sanitize / force-renderable" pathway is gated on it, and
            // every "do not mutate the live transform on a vanilla
            // bind" guarantee depends on it being false.
            var cloneFromSource =
                definition.Source != null &&
                !string.IsNullOrWhiteSpace(definition.Source);

            // Critical contract: an enabled-only mandela
            // (Source == null) MUST NOT touch the live transform
            // unless the definition itself supplies an explicit
            // override. We surface every override as a nullable so
            // the executor can pattern on HasValue without
            // re-implementing the rule.
            return new Plan
            {
                CloneFromSource = cloneFromSource,
                DestroyExistingTarget = cloneFromSource && existingTargetFound,
                EnsureParentChainExists = cloneFromSource,
                ZeroLocalTransformBeforeOverride = cloneFromSource,
                RunPrefabSanitizer = cloneFromSource,
                StripUnsupportedRuntimeComponents = cloneFromSource,
                // Force-renderable runs only on freshly-cloned subtrees
                // that the author intends to display. If the author
                // explicitly disables a brand-new clone we leave the
                // renderers in whatever state Instantiate produced —
                // SetActiveState=false will then keep the entire
                // subtree inactive anyway, so the renderer setup is
                // irrelevant.
                ForceRenderable = cloneFromSource && definition.Enabled != false,
                // Post-bind validation skips definitions the author
                // explicitly disabled so we don't log spurious "no
                // renderers" warnings against a deliberately hidden
                // target.
                RunPostBindValidation = definition.Enabled != false,
                OverrideLocalPosition = definition.LocalPosition,
                OverrideLocalRotation = definition.LocalRotation,
                OverrideLocalScale = definition.LocalScale,
                SetActiveState = definition.Enabled
            };
        }
    }
}
