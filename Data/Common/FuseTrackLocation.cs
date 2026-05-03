namespace FUSE.Data.Common
{
    public sealed class FuseTrackLocation
    {
        public string SegmentId { get; set; }
        public float? Normalized { get; set; }
        public float? Distance { get; set; }
        public string End { get; set; }
        public float Offset { get; set; }
    }
}
