using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZorChip8
{
    class PictureBoxWithInterpolation : PictureBox
    {
        public InterpolationMode InterpolationMode { get; set; }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            pe.Graphics.InterpolationMode = InterpolationMode;
            base.OnPaint(pe);
        }
    }
}
