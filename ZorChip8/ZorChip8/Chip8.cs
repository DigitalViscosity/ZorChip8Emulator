using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace ZorChip8
{
    class Chip8
    {
        private const int ScreenWidth = 64, ScreenHeight = 32;

        private Action<bool[,]> draw;
        private Action<int> beep; //make the sound from C# actions as a function call
        bool[,] buffer = new bool[ScreenWidth,ScreenHeight];

        //to reduce flicker, we delay clearing pixels by a frame
        bool[,] pendingClearBuffer = new bool[ScreenWidth,ScreenHeight];

        private bool needsRedraw = true;

        //registers
        byte[] V = new byte[16];
        //timers
        private byte Delay;
        //Address/PC
        private ushort I, PC = 0x200; //execution starts at address 0x200
        //stack
        private byte SP;
        ushort[] Stack = new ushort[16];

        //memory/ROM
        byte[] RAM = new byte[0x1000];

        //OpCodes
        private Dictionary<byte, Action<OpCodeData>> opCodes;
        private Dictionary<byte, Action<OpCodeData>> opCodesMisc;

        Random rnd = new Random();

        // track keys that are pressed
        HashSet<byte> pressedKeys = new HashSet<byte>();

        public Chip8(Action<bool[,]> draw, Action<int> beep)
        {
            this.draw = draw;
            this.beep = beep;

            WriteFont();

            opCodes = new Dictionary<byte, Action<OpCodeData>>
            {
                {0x0, ClearOrReturn},
                {0x1, Jump},
                {0x2, CallSubroutine},
                {0x3, SkipIfXEqual},
                {0x4, SkipIfXNotEqual},
                {0x5, SkipIfXEqualY},
                {0x6, SetX},
                {0x7, AddX},
                {0x8, Arithmetic},
                {0x9, SkipIfXNotEqualY},
                {0xA, SetI},
                {0xB, JumpWithOffset},
                {0xC, Rnd},
                {0xD, DrawSprite},
                {0xE, SkipOnKey},
                {0xF, Misc},
            };

            opCodesMisc = new Dictionary<byte, Action<OpCodeData>>
            {
                { 0x07, SetXToDelay },
                { 0x0A, WaitForKey },
                { 0x15, SetDelay },
                { 0x18, SetSound },
                { 0x1E, AddXToI },
                { 0x29, SetIForChar },
                { 0x33, BinaryCodedDecimal },
                { 0x55, SaveX },
                { 0x65, LoadX },
            };
        }

        void WriteFont()
        {
            var offset = 0x0;
            WriteFont(5 * offset++, Font.D0);
            WriteFont(5 * offset++, Font.D1);
            WriteFont(5 * offset++, Font.D2);
            WriteFont(5 * offset++, Font.D3);
            WriteFont(5 * offset++, Font.D4);
            WriteFont(5 * offset++, Font.D5);
            WriteFont(5 * offset++, Font.D6);
            WriteFont(5 * offset++, Font.D7);
            WriteFont(5 * offset++, Font.D8);
            WriteFont(5 * offset++, Font.D9);
            WriteFont(5 * offset++, Font.DA);
            WriteFont(5 * offset++, Font.DB);
            WriteFont(5 * offset++, Font.DC);
            WriteFont(5 * offset++, Font.DD);
            WriteFont(5 * offset++, Font.DE);
            WriteFont(5 * offset++, Font.DF);
        }

        void WriteFont(int address, long fontData)
        {
            RAM[address + 0] = (byte)((fontData & 0xF000000000) >> (8 * 4));
            RAM[address + 1] = (byte)((fontData & 0x00F0000000) >> (8 * 3));
            RAM[address + 2] = (byte)((fontData & 0x0000F00000) >> (8 * 2));
            RAM[address + 3] = (byte)((fontData & 0x000000F000) >> (8 * 1));
            RAM[address + 4] = (byte)((fontData & 0x00000000F0) >> (8 * 0));
        }

        public void LoadProgram(byte[] data)
        {
            Array.Copy(data, 0, RAM, 0x200, data.Length);
        }

        public void Tick()
        {
            //read 2bytes of OpCode
            var opCode = (ushort) (RAM[PC++] << 8 | RAM[PC++]);

            var op = new OpCodeData()
            {
                OpCode = opCode,
                NNN = (ushort) (opCode & 0x0FFF), // mask the opcode
                NN = (byte) (opCode & 0x00FF),
                N = (byte) (opCode & 0x000F),
                X = (byte) ((opCode & 0x0F00) >> 8),
                Y = (byte) ((opCode & 0x00F0) >> 4),
            };

            opCodes[(byte) (opCode >> 12)](op); // Loop up the opcode using the first nibble and call the correct action to execute
        }

        public void Tick60Hz()
        {
            if (Delay > 0)
                Delay--;
            if (needsRedraw)
            {
                needsRedraw = false;
                draw(buffer);
            }
        }

        // has it's own dictionary because it's full of random stuff
        void Misc(OpCodeData data)
        {
            if (opCodesMisc.ContainsKey(data.NN))
                opCodesMisc[data.NN](data);
        }

        public void KeyDown(byte key)
        {
            pressedKeys.Add(key);
        }

        public void KeyUp(byte key)
        {
            pressedKeys.Remove(key);
        }

        /// <summary>
        /// Handles 0x0... which either clears the screen or returns from a subroutine
        /// </summary>
        /// <param name="data"></param>
        void ClearOrReturn(OpCodeData data)
        {
            if (data.NN == 0xE0)
            {
                for (var x = 0; x < ScreenWidth; x++)
                {
                    for (var y = 0; y < ScreenHeight; y++)
                    {
                        buffer[x, y] = false;
                    }
                }
            }
            else if (data.NN == 0xEE)
                PC = Pop(); //don't be dumb hah
        }
        /// <summary>
        /// Jumps to a location nnn(not a subroutine, so old PC is not pushed to the stack
        /// </summary>
        /// <param name="data"></param>
        void Jump(OpCodeData data)
        {
            PC = data.NNN;
        }

        /// <summary>
        /// Jumps to a location nnn+v[0](PC not pushed to the stack)
        /// </summary>
        /// <param name="data"></param>
        void JumpWithOffset(OpCodeData data)
        {
            PC = (ushort) (data.NNN + V[0]);
        }

        /// <summary>
        /// Jumps to subroutine nnn (unlike Jump, this pushes the previous PC to the stack to allow return.)
        /// </summary>
        /// <param name="data"></param>
        void CallSubroutine(OpCodeData data)
        {
            Push(PC);
            PC = data.NNN;
        }

        /// <summary>
        /// Skips the next instruction (2 bytes) if V[x] == nn
        /// </summary>
        /// <param name="data"></param>
        void SkipIfXEqual(OpCodeData data)
        {
            if (V[data.X] == data.NN)
            {
                PC += 2;
            }
        }

        /// <summary>
        /// Skips the next instruction (2 bytes if V[x] != nn
        /// </summary>
        /// <param name="data"></param>
        void SkipIfXNotEqual(OpCodeData data)
        {
            if (V[data.X]!=data.NN)
            {
                PC += 2;
            }
        }

        /// <summary>
        /// Skips the next instruction (2 bytes) if V[x] == V[y]
        /// </summary>
        /// <param name="data"></param>
        void SkipIfXEqualY(OpCodeData data)
        {
            if (V[data.X]==V[data.Y])
            {
                PC += 2;
            }
        }

        /// <summary>
        /// Skips the next instruction (2 bytes) if V[x] != V[y]
        /// </summary>
        /// <param name="data"></param>
        void SkipIfXNotEqualY(OpCodeData data)
        {
            if (V[data.X]!=V[data.Y])
            {
                PC += 2;
            }
        }

        /// <summary>
        /// Sets V[x] == NN
        /// </summary>
        /// <param name="data"></param>
        void SetX(OpCodeData data)
        {
            V[data.X] = data.NN;
        }

        void AddX(OpCodeData data)
        {
            V[data.X] += data.NN; //doesn't handle overflow yet.... will do it later
            // TODO: Handle Overflow
        }

        /// <summary>
        /// Sets V[x] to V[y]
        /// </summary>
        /// <param name="data"></param>
        void Arithmetic(OpCodeData data)
        {
            switch (data.N)
            {
                case 0x0:
                    V[data.X] = V[data.Y]; // set
                    break;
                case 0x1:
                    V[data.X] |= V[data.Y]; // or
                    break;
                case 0x2:
                    V[data.X] &= V[data.Y]; // and
                    break;
                case 0x3:
                    V[data.X] ^= V[data.Y]; // xor
                    break;
                case 0x4:
                    V[0xF] = (byte)(V[data.X] + V[data.Y] > 0xFF ? 1 : 0); // Set flag if we overflow "set VF to 01 if carry 
                    V[data.X] += V[data.Y];
                    break;
                case 0x5:
                    V[0xF] = (byte) (V[data.X] > V[data.Y] ? 1 : 0); // set flag on underflow
                    V[data.X] -= V[data.Y];
                    break;
                case 0x6:
                    V[0xF] = (byte) ((V[data.X] & 0x1) != 0 ? 1 : 0); //set flag if we shifted a 1 off the end
                    V[data.X] /= 2; //shift right
                    break;
                case 0x7:
                    V[0xF] = (byte) (V[data.Y] > V[data.X] ? 1 : 0); //note this is Y - X
                    V[data.Y] -= V[data.X];
                    break;
                case 0xE:
                    V[0xF] = (byte) ((V[data.X] & 0xF) != 0 ? 1 : 0);
                    V[data.X] *= 2; //shift left
                    break;
            }
        }

        /// <summary>
        /// Sets the I register
        /// </summary>
        /// <param name="data"></param>
        void SetI(OpCodeData data)
        {
            I = data.NNN;
        }

        /// <summary>
        /// AND a random number with nn and store in V[x]
        /// </summary>
        /// <param name="data"></param>
        void Rnd(OpCodeData data)
        {
            V[data.X] = (byte) (rnd.Next(0, 256) & data.NN);
        }

        /// <summary>
        /// Draws an n-byte sprite from register I at V[x], V[y]. Sets V[0xF] if it collides
        /// </summary>
        /// <param name="data"></param>
        void DrawSprite(OpCodeData data)
        {
            var startX = V[data.X];
            var startY = V[data.Y];

            //write any pending clears
            for (int x = 0; x < ScreenWidth; x++)
            {
                for (int y = 0; y < ScreenHeight; y++)
                {
                    if (pendingClearBuffer[x,y])
                    {
                        if (buffer[x,y])
                        {
                            needsRedraw = true;
                        }

                        pendingClearBuffer[x, y] = false;
                        buffer[x, y] = false;
                    }
                }
            }

            V[0xF] = 0;
            for (int i = 0; i < data.N; i++)
            {
                var spriteLine = RAM[I + i]; //A line of the sprite to render

                for (int bit = 0; bit < 8; bit++)
                {
                    var x = (startX + bit) % ScreenWidth;
                    var y = (startY + i) % ScreenHeight;

                    var spriteBit = ((spriteLine >> (7 - bit)) & 1);
                    var oldBit = buffer[x, y] ? 1 : 0;

                    if (oldBit != spriteBit)
                        needsRedraw = true;

                    // New bit is XOR of existing and new.
                    var newBit = oldBit ^ spriteBit;

                    if (newBit != 0)
                        buffer[x, y] = true;
                    else // otherwise write a pending clear
                        pendingClearBuffer[x, y] = true;

                    // If we wiped out a pixel, set flag for collision
                    if (oldBit != 0 && newBit == 0)
                        V[0xF] = 1;
                }
            }
        }

        /// <summary>
        /// Skips the next instruction based on the key at V[x] being pressed/not pressed
        /// </summary>
        /// <param name="data"></param>
        void SkipOnKey(OpCodeData data)
        {
            if (
                (data.NN == 0x9E && pressedKeys.Contains(V[data.X])) // 9E = IfKeyPressed
                || (data.NN == 0xA1 && !pressedKeys.Contains(V[data.X])) // A1 = IfKeyNotPressed
            )
                PC += 2;
        }

        void WaitForKey(OpCodeData data)
        {
            // If we have a key pressed, store it and more on.
            if (pressedKeys.Count != 0)
                V[data.X] = pressedKeys.First();
            else
                //otherwise, wind the PC back so we will keep executing this instruction
                PC -= 2;
        }

        /// <summary>
        /// Sets V[x] to equal the Delay register
        /// </summary>
        /// <param name="data"></param>
        void SetXToDelay(OpCodeData data)
        {
            V[data.X] = Delay;
        }

        /// <summary>
        /// Sets the delay register to V[x]
        /// </summary>
        /// <param name="data"></param>
        void SetDelay(OpCodeData data)
        {
            Delay = V[data.X];
        }

        /// <summary>
        /// Play sound for V[x] 60ths of a second
        /// </summary>
        /// <param name="data"></param>
        void SetSound(OpCodeData data)
        {
            beep((int) (V[data.X] * (1000f / 60)));
        }

        /// <summary>
        /// Adds V[x] to register I
        /// </summary>
        /// <param name="data"></param>
        void AddXToI(OpCodeData data)
        {
            I += V[data.X];
        }

        /// <summary>
        /// Sets I to the correct location of the font sprite V[x]
        /// </summary>
        /// <param name="data"></param>
        void SetIForChar(OpCodeData data)
        {
            I = (ushort) (V[data.X] * 5); // 0 is at 0x0, 1 is at 0x5...
        }

        /// <summary>
        /// Store the binary-coded decimal equivalent of the value stored in register VX at addresses I, I + 1, and I + 2
        /// </summary>
        /// <param name="data"></param>
        void BinaryCodedDecimal(OpCodeData data)
        {
            RAM[I + 0] = (byte) ((V[data.X] / 100) % 10);
            RAM[I + 1] = (byte)((V[data.X] / 10) % 10);
            RAM[I + 2] = (byte)(V[data.X] % 10); 
        }

        void SaveX(OpCodeData data)
        {
            for (int i = 0; i <= data.X; i++)
            {
                RAM[I + i] = V[i];
            }
        }

        void LoadX(OpCodeData data)
        {
            for (int i = 0; i <= data.X; i++)
            {
                V[i] = RAM[I + i];
            }
        }
        /// <summary>
        ///  Pushes a 16bit value onto the stack, inc the SP
        /// </summary>
        /// <param name="value"></param>
        void Push(ushort value)
        {
            Stack[SP++] = value;
        }

        /// <summary>
        /// Retrieves a 16bit value from the stack, dec the SP
        /// </summary>
        /// <returns>ushort stack</returns>
        ushort Pop()
        {
            return Stack[--SP];
        }
    }
}
