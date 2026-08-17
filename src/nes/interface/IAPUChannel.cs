namespace NES
{
    public interface IAPUChannel
    {
        /// <summary>
        /// Raw DAC input for this channel (0-15).
        /// </summary>
        public int Sample();
    }
}
