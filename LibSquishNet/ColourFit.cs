namespace LibSquishNet
{
    public class ColourFit
    {
        protected ColourSet MColours;
        protected SquishFlags MFlags;

        public ColourFit(ColourSet colours, SquishFlags flags)
        {
            MColours = colours;
            MFlags = flags;
        }

        public void Compress(ref byte[] block, int offset)
        {
            var isDxt1 = ((MFlags & SquishFlags.KDxt1) != 0);

            if (isDxt1)
            {
                Compress3(ref block, offset);

                if (!MColours.IsTransparent) {
                    Compress4(ref block, offset);
                }
            }
            else
            {
                Compress4(ref block, offset);
            }
        }

        public virtual void Compress3(ref byte[] block, int offset) { }
        public virtual void Compress4(ref byte[] block, int offset) { }
    }
}
