namespace LibSquishNet
{
    public struct SingleColourLookup
    {
        public readonly SourceBlock[] Sources;

        public SingleColourLookup(SourceBlock a, SourceBlock b)
        {
            Sources = new[] { a, b };
        }
    };
}
