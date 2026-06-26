using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FUSE.Editor.Screen.PropertyRenderers
{
    /// <summary>
    /// Base interface for rendering a specific property type in the properties panel.
    /// Implementations can provide custom UI for different property types like dropdowns,
    /// color pickers, object selectors, etc.
    /// </summary>
    public interface IPropertyRenderer
    {
        /// <summary>
        /// Determines if this renderer can handle the given property type.
        /// </summary>
        /// <param name="propertyType">The type of the property to render</param>
        /// <returns>True if this renderer can handle this type, false otherwise</returns>
        bool CanRender(Type propertyType);

        /// <summary>
        /// Renders the property editor UI for the given property.
        /// </summary>
        /// <param name="rect">The rect to draw the property in</param>
        /// <param name="propertyName">The name of the property</param>
        /// <param name="propertyType">The type of the property</param>
        /// <param name="currentValue">The current value of the property</param>
        /// <param name="labelStyle">The label text style</param>
        /// <param name="valueStyle">The value/input style</param>
        /// <returns>A tuple indicating if the value changed and the new value if changed</returns>
        (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                        object currentValue, GUIStyle labelStyle, GUIStyle valueStyle);

        /// <summary>
        /// Gets the height needed for this property renderer (for scrolling calculations).
        /// Most properties return RowHeight, but some might need more space.
        /// </summary>
        /// <param name="propertyType">The type of the property</param>
        /// <returns>The height needed in pixels</returns>
        float GetPropertyHeight(Type propertyType);
    }

    /// <summary>
    /// Registry for property renderers. Allows external code to register custom renderers
    /// for specific property types, enabling extensibility for custom UI elements like
    /// dropdowns, color pickers, and object selectors.
    /// </summary>
    public sealed class PropertyRendererRegistry
    {
        private static PropertyRendererRegistry _instance;
        private readonly List<IPropertyRenderer> _renderers = new List<IPropertyRenderer>();
        private const float DefaultRowHeight = 22f;

        private IPropertyRenderer defaultRenderer = new DefaultPropertyRenderer();

        public static PropertyRendererRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PropertyRendererRegistry();
                    _instance.RegisterDefaultRenderers();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Registers a custom property renderer. Renderers are checked in registration order,
        /// so more specific renderers should be registered first.
        /// </summary>
        /// <param name="renderer">The renderer to register</param>
        public void RegisterRenderer(IPropertyRenderer renderer)
        {
            if (renderer != null && !_renderers.Contains(renderer))
            {
                _renderers.Add(renderer); // Insert at beginning for priority
            }
        }

        /// <summary>
        /// Unregisters a property renderer.
        /// </summary>
        /// <param name="renderer">The renderer to unregister</param>
        /// <returns>True if the renderer was found and removed, false otherwise</returns>
        public bool UnregisterRenderer(IPropertyRenderer renderer)
        {
            return _renderers.Remove(renderer);
        }

        /// <summary>
        /// Finds a renderer that can handle the given property type.
        /// </summary>
        /// <param name="propertyType">The type of the property to render</param>
        /// <returns>A renderer capable of rendering this type, or null if none found</returns>
        public IPropertyRenderer GetRenderer(Type propertyType)
        {
            foreach (var renderer in _renderers)
            {
                if (renderer.CanRender(propertyType))
                {
                    return renderer;
                }
            }
            return defaultRenderer;
        }

        /// <summary>
        /// Gets the height needed for a property of the given type.
        /// </summary>
        /// <param name="propertyType">The type of the property</param>
        /// <returns>The height needed in pixels</returns>
        public float GetPropertyHeight(Type propertyType)
        {
            var renderer = GetRenderer(propertyType);
            return renderer?.GetPropertyHeight(propertyType) ?? DefaultRowHeight;
        }

        /// <summary>
        /// Renders a property using the appropriate renderer.
        /// </summary>
        /// <param name="rect">The rect to draw the property in</param>
        /// <param name="propertyName">The name of the property</param>
        /// <param name="propertyType">The type of the property</param>
        /// <param name="currentValue">The current value of the property</param>
        /// <param name="labelStyle">The label text style</param>
        /// <param name="valueStyle">The value/input style</param>
        /// <returns>A tuple indicating if the value changed and the new value if changed</returns>
        public (bool changed, object newValue) RenderProperty(Rect rect, string propertyName, Type propertyType,
                                                               object currentValue, GUIStyle labelStyle, GUIStyle valueStyle)
        {
            var renderer = GetRenderer(propertyType);
            if (renderer != null)
            {
                return renderer.RenderProperty(rect, propertyName, propertyType, currentValue, labelStyle, valueStyle);
            }

            // Fallback: use default renderer
            return defaultRenderer.RenderProperty(rect, propertyName, propertyType, currentValue, labelStyle, valueStyle);
        }

        private void RegisterDefaultRenderers()
        {
            // Register built-in renderers in priority order
            RegisterRenderer(new TrackNodePropertyRenderer());
            RegisterRenderer(new EnumPropertyRenderer());
            RegisterRenderer(new Vector3PropertyRenderer());
            RegisterRenderer(new Vector2PropertyRenderer());
            RegisterRenderer(new QuaternionPropertyRenderer());
            RegisterRenderer(new BoolPropertyRenderer());
            RegisterRenderer(new IntPropertyRenderer());
            RegisterRenderer(new FloatPropertyRenderer());
            RegisterRenderer(new StringPropertyRenderer());
        }
    }
}
