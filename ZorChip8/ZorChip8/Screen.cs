using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZorChip8
{
    public partial class Screen : Form
    {
        readonly Chip8 chip8;
        readonly Bitmap screen;
        private readonly string ROM = "INVADERS";

        //timing
        readonly Stopwatch stopWatch = Stopwatch.StartNew();
        private readonly TimeSpan targetElapsedTime60Hz = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        private readonly TimeSpan targetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 1000);
        private TimeSpan lastTime;

        //keymapping
        private Dictionary<Keys, byte> keyMapping = new Dictionary<Keys, byte>
        {
            {Keys.D1, 0x1},
            {Keys.D2, 0x2},
            {Keys.D3, 0x3},
            {Keys.D4, 0xC},
            {Keys.Q, 0x4},
            {Keys.W, 0x5},
            {Keys.E, 0x6},
            {Keys.R, 0xD},
            {Keys.A, 0x7},
            {Keys.S, 0x8},
            {Keys.D, 0x9},
            {Keys.F, 0xE},
            {Keys.Z, 0xA},
            {Keys.X, 0x0},
            {Keys.C, 0xB},
            {Keys.V, 0xF},
        };

        public Screen()
        {
            InitializeComponent();

            screen = new Bitmap(64,32);
            pbScreen.Image = screen;

            chip8 = new Chip8(Draw, Beep);
            chip8.LoadProgram(File.ReadAllBytes(ROM));

            KeyDown += SetKeyDown;
            KeyUp += SetKeyUp;

        }

        protected override void OnLoad(EventArgs e)
        {
            StartGameLoop();
        }

        void Draw(bool[,] buffer)
        {
            var bits = screen.LockBits(new Rectangle(0, 0, screen.Width, screen.Height), ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb);

            unsafe
            {
                byte* pointer = (byte*) bits.Scan0;

                for (int y = 0; y < screen.Height; y++)
                {
                    for (int x = 0; x < screen.Width; x++)
                    {
                        pointer[0] = 0;
                        pointer[1] = buffer[x, y] ? (byte) 0x64 : (byte) 0;
                        pointer[2] = 0;
                        pointer[3] = 255;

                        pointer += 4; // 4 bytes per pixel
                    }
                }
            }

            screen.UnlockBits(bits);
        }

        void Beep(int milliseconds)
        {
            Console.Beep(500, milliseconds);
        }
        void SetKeyDown(object sender, KeyEventArgs e)
        {
            if (keyMapping.ContainsKey(e.KeyCode))
            {
                chip8.KeyDown(keyMapping[e.KeyCode]);
            }
        }

        void SetKeyUp(object sender, KeyEventArgs e)
        {
            if(keyMapping.ContainsKey(e.KeyCode))
                chip8.KeyUp(keyMapping[e.KeyCode]);
        }

        void StartGameLoop()
        {
            Task.Run(GameLoop);
        }

        Task GameLoop()
        {
            while (true)
            {
                var currentTime = stopWatch.Elapsed;
                var elapsedTime = currentTime - lastTime;

                while (elapsedTime >= targetElapsedTime60Hz)
                {
                    try
                    {
                        this.Invoke((Action) Tick60Hz);
                    }
                    catch(Exception e)
                    { }

                    elapsedTime -= targetElapsedTime60Hz;
                    lastTime += targetElapsedTime60Hz;
                }

                try
                {
                    this.Invoke((Action)Tick);
                }
                catch (Exception e)
                {
                }
                

                Thread.Sleep(targetElapsedTime);
            }
        }

        void Tick() => chip8.Tick();
        void Tick60Hz()
        {
            chip8.Tick60Hz();
            pbScreen.Refresh();
        }

        private void Screen_Load(object sender, EventArgs e)
        {

        }
    }
}
