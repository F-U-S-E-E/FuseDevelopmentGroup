namespace RAIL.Editor
{
    public interface IRailSelectionProvider
    {
        string SelectedObjectId { get; }
        string SelectedObjectType { get; }
        void SelectObject(string id, string type);
        void ClearSelection();
    }
}
