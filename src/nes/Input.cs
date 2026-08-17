namespace NES
{
    [Flags]
    public enum NESButtons : byte
    {
        None = 0,
        A = 1 << 0,
        B = 1 << 1,
        Select = 1 << 2,
        Start = 1 << 3,
        Up = 1 << 4,
        Down = 1 << 5,
        Left = 1 << 6,
        Right = 1 << 7,
    }

    public struct ControllerState
    {
        public byte controllerState;
        public byte controllerShift;
    }

    public struct InputState
    {
        public bool strobe;
        public ControllerState controller1;
        public ControllerState controller2;
    }

    public class Input
    {
        // state
        public InputState State
        {
            get => state;
            set => state = value;
        }
        private InputState state = new InputState();

        private Bus bus;

        // Called once per emulated frame with the current button state, as
        // read by whatever is driving the emulator (e.g. Emulator.Input).
        public void UpdateController(int index, byte buttons)
        {
            if(index == 0) state.controller1.controllerState = buttons;
            else if(index == 1) state.controller2.controllerState = buttons;
        }

        public void Write4016(byte value)
        {
            state.strobe = (value & 1) != 0;
            if (state.strobe)
            {
                state.controller1.controllerShift = state.controller1.controllerState;
                state.controller2.controllerShift = state.controller2.controllerState;
            }
        }

        public byte Read4016()
        {
            return ReadController(ref state.controller1);
        }

        public byte Read4017()
        {
            return ReadController(ref state.controller2);
        }

        private byte ReadController(ref ControllerState c)
        {
            byte result;
            if (state.strobe)
            {
                c.controllerShift = c.controllerState;
                result = (byte)(c.controllerState & 1);
            }
            else
            {
                result = (byte)(c.controllerShift & 1);
                c.controllerShift = (byte)((c.controllerShift >> 1) | 0x80);
            }
            return result;
        }

        public Input(Bus bus)
        {
            this.bus = bus;
        }
    }
}
