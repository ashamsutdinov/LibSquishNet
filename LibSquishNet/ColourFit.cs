namespace LibSquishNet
{
    public class ColourFit
    {
        protected readonly ColourSet MColours;

        protected readonly SquishFlags MFlags;

        protected ColourFit(ColourSet colours, SquishFlags flags)
        {
            MColours = colours;
            MFlags = flags;
        }

        public void Compress(ref byte[] block, int offset)
        {
            var isDxt1 = (MFlags & SquishFlags.KDxt1) != 0;

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

        protected virtual void Compress3(ref byte[] block, int offset)
        {
            
        }

        protected virtual void Compress4(ref byte[] block, int offset)
        {
            
        }
    }
}
