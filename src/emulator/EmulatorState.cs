namespace Emulator
{
    public enum TvSystemMode
    {
        Auto,
        ForceNTSC,
        ForcePAL
    }

    public class EmulatorState
    {
        public static NES.NES? NES;

        public static int scale = 2;
        public static TvSystemMode tvSystemMode = TvSystemMode.Auto;
        public static string romPath = "";
        public static bool fpsEnable = false;
        public static bool insertingRom = false;
        public static bool showMenuBar = true;
        public static bool paused = false;
        public static bool raylibLog = false;


        public static void ParseStateArgs(string[] args)
        {
            if (args.Length >= 1)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--nes")
                    {
                        if (i + 1 < args.Length)
                        {
                            romPath = args[i + 1];
                            if (!File.Exists(romPath))
                            {
                                Console.WriteLine("ROM path \"" + romPath + "\" is invalid");
                                Environment.Exit(1);
                            }
                            insertingRom = true;
                            i += 1;
                        }
                        else
                        {
                            Console.WriteLine("No ROM passed in");
                            Console.WriteLine("Usage: --nes <string:rom>");
                            //Environment.Exit(1);
                        }
                    }
                    if (args[i] == "-f" || args[i] == "--fps")
                    {
                        fpsEnable = true;
                    }
                    if (args[i] == "-s" || args[i] == "--scale")
                    {
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedScale))
                        {
                            scale = parsedScale;
                            i += 1;
                        }
                        else
                        {
                            Console.WriteLine("No scale integer passed in");
                            Console.WriteLine("Usage: -s <int:scale>, --scale <int:scale>");
                            Environment.Exit(1);
                        }
                    }
                    if (args[i] == "-rl" || args[i] == "-raylib-log")
                    {
                        raylibLog = true;
                    }
                    if (args[i] == "-r" || args[i] == "--region")
                    {
                        if (i + 1 < args.Length)
                        {
                            string region = args[i + 1].ToLowerInvariant();
                            if (region == "ntsc")
                            {
                                tvSystemMode = TvSystemMode.ForceNTSC;
                            }
                            else if (region == "pal")
                            {
                                tvSystemMode = TvSystemMode.ForcePAL;
                            }
                            else if (region == "auto")
                            {
                                tvSystemMode = TvSystemMode.Auto;
                            }
                            else
                            {
                                Console.WriteLine("Invalid region \"" + args[i + 1] + "\"");
                                Console.WriteLine("Usage: -r <ntsc|pal|auto>, --region <ntsc|pal|auto>");
                                Environment.Exit(1);
                            }
                            i += 1;
                        }
                        else
                        {
                            Console.WriteLine("No region passed in");
                            Console.WriteLine("Usage: -r <ntsc|pal|auto>, --region <ntsc|pal|auto>");
                            Environment.Exit(1);
                        }
                    }
                }
            }
        }
    }
}