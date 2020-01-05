using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZorChip8
{
    struct OpCodeData
    {
        public ushort OpCode;
        public ushort NNN;
        public byte NN, X, Y, N;

        public override string ToString()
        {
            return $"{OpCode:X4} (X: {X:X}, Y: {Y:X}, N: {N:X},NN: {NN:X2}, NNN: {NNN:X3}"; //pretty output for debugging
        }
    }
}
