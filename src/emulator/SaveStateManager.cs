using NES;

namespace Emulator
{
    public class SaveStateManager
    {
        private const string SAVESTATES_DIR = "savestates";
        private const string SLOT_FORMAT = "{0}_slot{1}.state";

        public const int NUM_SLOTS = 10;
        public const int QUICKSAVE_SLOT = -1;

        /// <returns>If loading the state was successful</returns>
        public static bool LoadState(NES.NES nes, int slot)
        {
            if (!Directory.Exists(SAVESTATES_DIR))
            {
                return false;
            }

            var cart = nes.Cartridge;
            string filename = string.Format(SLOT_FORMAT, Convert.ToHexString(cart.romHash), slot);
            string filepath = Path.Combine(SAVESTATES_DIR, filename);
            if (!File.Exists(filepath))
            {
                return false;
            }

            try
            {
                var state = new SaveState(nes);
                using (var stream = File.OpenRead(filepath))
                {
                    state.Load(stream);
                }
                state.Apply(nes);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception during savestate load: {e.Message}");
                return false;
            }
            return true;
        }

        public static void SaveState(NES.NES nes, int slot)
        {
            if (!Directory.Exists(SAVESTATES_DIR))
            {
                Directory.CreateDirectory(SAVESTATES_DIR);
            }

            var state = new SaveState(nes);
            var cart = nes.Cartridge;

            string filename = string.Format(SLOT_FORMAT, Convert.ToHexString(cart.romHash), slot);
            string filepath = Path.Combine(SAVESTATES_DIR, filename);

            using (var stream = File.Open(filepath, FileMode.Create))
            {
                state.Save(stream);
            }
        }
    }
}