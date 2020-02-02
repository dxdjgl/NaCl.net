﻿using System;
using NaCl.Internal; 

namespace NaCl
{
    public class Poly1305 : IDisposable
    {
        public const int BlockLength = 16;
        public const int KeyLength = 32;
        public const int HashLength = BlockLength;
        
        UInt32[] r = new UInt32[5];
        UInt32[] h = new UInt32[5];
        UInt32[] pad = new UInt32 [4];
        int leftover;
        byte[] buffer = new byte[BlockLength];
        bool final;
        
        public Poly1305(ReadOnlySpan<byte> key)
        {
            Initialize();
            SetKey(key);
        }

        public Poly1305() => Initialize();
        
        public void Dispose() => Reset();

        public void Reset()
        {
            Array.Clear(r, 0, 5);
            Array.Clear(h, 0, 5);
            Array.Clear(pad, 0, 4);
            Array.Clear(buffer, 0, BlockLength);
                      
            leftover = 0;
            final = false;
        }
        
        public void SetKey(ReadOnlySpan<byte> key)
        {
            // r &= 0xffffffc0ffffffc0ffffffc0fffffff - wiped after finalization
            r[0] = (Common.Load32(key, 0)) & 0x3ffffff;
            r[1] = (Common.Load32(key, 3) >> 2) & 0x3ffff03;
            r[2] = (Common.Load32(key, 6) >> 4) & 0x3ffc0ff;
            r[3] = (Common.Load32(key, 9) >> 6) & 0x3f03fff;
            r[4] = (Common.Load32(key, 12) >> 8) & 0x00fffff;
            
            // save pad for later
            pad[0] = Common.Load32(key, 16);
            pad[1] = Common.Load32(key, 20);
            pad[2] = Common.Load32(key, 24);
            pad[3] = Common.Load32(key, 28);
        }

        public void SetKey(byte[] key, int offset) => SetKey(new Span<byte>(key, offset, KeyLength));

        private void Initialize()
        {
            h[0] = 0;
            h[1] = 0;
            h[2] = 0;
            h[3] = 0;
            h[4] = 0;
            Array.Clear(this.buffer, 0, BlockLength);
            
            leftover = 0;
            final = false;
        }
        
        void Blocks(ReadOnlySpan<byte> m, int count)
        {
            UInt32 hibit = final ? 0U : (1U << 24); // 1 << 128
            UInt32 r0, r1, r2, r3, r4;
            UInt32 s1, s2, s3, s4;
            UInt32 h0, h1, h2, h3, h4;
            UInt64 d0, d1, d2, d3, d4;
            UInt32 c;

            r0 = r[0];
            r1 = r[1];
            r2 = r[2];
            r3 = r[3];
            r4 = r[4];

            s1 = r1 * 5;
            s2 = r2 * 5;
            s3 = r3 * 5;
            s4 = r4 * 5;

            h0 = h[0];
            h1 = h[1];
            h2 = h[2];
            h3 = h[3];
            h4 = h[4];

            while (count >= BlockLength)
            {
                /* h += m[i] */
                h0 += (Common.Load32(m, 0)) & 0x3ffffff;
                h1 += (Common.Load32(m, 3) >> 2) & 0x3ffffff;
                h2 += (Common.Load32(m, 6) >> 4) & 0x3ffffff;
                h3 += (Common.Load32(m, 9) >> 6) & 0x3ffffff;
                h4 += (Common.Load32(m, 12) >> 8) | hibit;

                /* h *= r */
                d0 = ((UInt64) h0 * r0) + ((UInt64) h1 * s4) +
                     ((UInt64) h2 * s3) + ((UInt64) h3 * s2) +
                     ((UInt64) h4 * s1);
                d1 = ((UInt64) h0 * r1) + ((UInt64) h1 * r0) +
                     ((UInt64) h2 * s4) + ((UInt64) h3 * s3) +
                     ((UInt64) h4 * s2);
                d2 = ((UInt64) h0 * r2) + ((UInt64) h1 * r1) +
                     ((UInt64) h2 * r0) + ((UInt64) h3 * s4) +
                     ((UInt64) h4 * s3);
                d3 = ((UInt64) h0 * r3) + ((UInt64) h1 * r2) +
                     ((UInt64) h2 * r1) + ((UInt64) h3 * r0) +
                     ((UInt64) h4 * s4);
                d4 = ((UInt64) h0 * r4) + ((UInt64) h1 * r3) +
                     ((UInt64) h2 * r2) + ((UInt64) h3 * r1) +
                     ((UInt64) h4 * r0);

                // (partial) h %= p 
                c = (UInt32) (d0 >> 26);
                h0 = (UInt32) d0 & 0x3ffffff;
                d1 += c;
                c = (UInt32) (d1 >> 26);
                h1 = (UInt32) d1 & 0x3ffffff;
                d2 += c;
                c = (UInt32) (d2 >> 26);
                h2 = (UInt32) d2 & 0x3ffffff;
                d3 += c;
                c = (UInt32) (d3 >> 26);
                h3 = (UInt32) d3 & 0x3ffffff;
                d4 += c;
                c = (UInt32) (d4 >> 26);
                h4 = (UInt32) d4 & 0x3ffffff;
                h0 += c * 5;
                c = (h0 >> 26);
                h0 = h0 & 0x3ffffff;
                h1 += c;

                m = m.Slice(BlockLength);
                count -= BlockLength;
            }

            h[0] = h0;
            h[1] = h1;
            h[2] = h2;
            h[3] = h3;
            h[4] = h4;
        }

        public void Final(Span<byte> output)
        {
            UInt32 h0, h1, h2, h3, h4, c;
            UInt32 g0, g1, g2, g3, g4;
            UInt64 f;
            UInt32 mask;

            // process the remaining block
            if (leftover > 0)
            {
                int i = leftover;

                buffer[i++] = 1;
                for (; i < BlockLength; i++)
                {
                    buffer[i] = 0;
                }

                final = true;
                Blocks(buffer, BlockLength);
            }

            // fully carry h
            h0 = h[0];
            h1 = h[1];
            h2 = h[2];
            h3 = h[3];
            h4 = h[4];

            c = h1 >> 26;
            h1 = h1 & 0x3ffffff;
            h2 += c;
            c = h2 >> 26;
            h2 = h2 & 0x3ffffff;
            h3 += c;
            c = h3 >> 26;
            h3 = h3 & 0x3ffffff;
            h4 += c;
            c = h4 >> 26;
            h4 = h4 & 0x3ffffff;
            h0 += c * 5;
            c = h0 >> 26;
            h0 = h0 & 0x3ffffff;
            h1 += c;

            /* compute h + -p */
            g0 = h0 + 5;
            c = g0 >> 26;
            g0 &= 0x3ffffff;
            g1 = h1 + c;
            c = g1 >> 26;
            g1 &= 0x3ffffff;
            g2 = h2 + c;
            c = g2 >> 26;
            g2 &= 0x3ffffff;
            g3 = h3 + c;
            c = g3 >> 26;
            g3 &= 0x3ffffff;
            g4 = h4 + c - (1U << 26);

            // select h if h < p, or h + -p if h >= p 
            mask = (g4 >> ((sizeof(UInt32) * 8) - 1)) - 1;
            g0 &= mask;
            g1 &= mask;
            g2 &= mask;
            g3 &= mask;
            g4 &= mask;
            mask = ~mask;

            h0 = (h0 & mask) | g0;
            h1 = (h1 & mask) | g1;
            h2 = (h2 & mask) | g2;
            h3 = (h3 & mask) | g3;
            h4 = (h4 & mask) | g4;

            /* h = h % (2^128) */
            h0 = ((h0) | (h1 << 26)) & 0xffffffff;
            h1 = ((h1 >> 6) | (h2 << 20)) & 0xffffffff;
            h2 = ((h2 >> 12) | (h3 << 14)) & 0xffffffff;
            h3 = ((h3 >> 18) | (h4 << 8)) & 0xffffffff;

            /* mac = (h + pad) % (2^128) */
            f = (UInt64) h0 + pad[0];
            h0 = (UInt32) f;
            f = (UInt64) h1 + pad[1] + (f >> 32);
            h1 = (UInt32) f;
            f = (UInt64) h2 + pad[2] + (f >> 32);
            h2 = (UInt32) f;
            f = (UInt64) h3 + pad[3] + (f >> 32);
            h3 = (UInt32) f;

            Common.Store(output, 0, h0);
            Common.Store(output, 4, h1);
            Common.Store(output, 8, h2);
            Common.Store(output, 12, h3);

            Initialize();
        }
        
        public void Update(ReadOnlySpan<byte> m)
        {
            int count = m.Length;

            // handle leftover
            if (leftover != 0)
            {
                int want = (BlockLength - leftover);

                if (want > count)
                    want = count;

                for (int i = 0; i < want; i++)
                    buffer[leftover + i] = m[i];

                count -= want;
                m = m.Slice(want);
                leftover += want;

                if (leftover < BlockLength)
                    return;

                Blocks(buffer, BlockLength);
                leftover = 0;
            }

            // process full blocks 
            if (count >= BlockLength)
            {
                int want = (count & ~(BlockLength - 1));

                Blocks(m, want);
                m = m.Slice(want);
                count -= want;
            }

            // store leftover
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                    buffer[leftover + i] = m[i];

                leftover += count;
            }
        }

        public  void Update(byte[] array, int offset, int count)
        {
            var span = new Span<byte>(array, offset, count);
            Update(span);
        }

        public byte[] Final()
        {
            byte[] hash = new byte[BlockLength];
            Final(hash);
                
            return hash;
        }
        
        public bool Verify(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> input)
        {
            Update(input);
            byte[] correct = Final();

            return SafeComparison.Verify16(hash, correct);
        }

        public bool Verify(byte[] hash, int hashOffset, byte[] input, int inputOffset, int inputCount)
        {
            var hashSpan = new Span<byte>(hash, hashOffset, HashLength);
            var inputSpan = new Span<byte>(input, inputOffset, inputCount);

            return Verify(hash, input);
        }
    }
}