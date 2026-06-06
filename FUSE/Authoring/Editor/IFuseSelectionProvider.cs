namespace FUSE.Authoring.Editor
{
    public interface IFuseSelectionProvider
    {
        string SelectedObjectId { get; }
        string SelectedObjectType { get; }
        void SelectObject(string id, string type);
        void ClearSelection();
    }
}
