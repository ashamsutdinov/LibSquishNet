namespace LibSquishNet
{
    public struct SourceBlock
    {
        public readonly byte Start;

        public readonly byte End;

        public readonly byte Error;

        public SourceBlock(byte s, byte e, byte err)
        {
            Start = s;
            End = e;
            Error = err;
        }
    };
}