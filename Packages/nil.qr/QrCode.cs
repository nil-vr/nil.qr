
using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace Nil.Qr
{
    public class QrCode : UdonSharpBehaviour
    {
        public string Content = "";
        [NonSerialized]
        bool isOwnTexture;

#if UNITY_EDITOR
        void OnValidate()
        {
            var renderer = this.GetComponent<RawImage>();
            if (renderer == null)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    var r = this.GetComponent<RawImage>();
                    if (r == null)
                    {
                        r = this.gameObject.AddComponent<RawImage>();
                        r.uvRect = new Rect(0, 1, 1, -1);

                        var materialPath = UnityEditor.AssetDatabase.GUIDToAssetPath("66793f255fdda214d9802c731a052a82");
                        if (materialPath != null)
                        {
                            var material = (Material)UnityEditor.AssetDatabase.LoadAssetAtPath(materialPath, typeof(Material));
                            if (material != null)
                            {
                                r.material = material;
                            }
                        }
                    }
                    OnValidate();
                };
                return;
            }

            if (isOwnTexture)
            {
                var old = renderer.texture;
                renderer.texture = null;
                DestroyImmediate(old);
            }

            var texture = Render(Content ?? "");
            renderer.texture = texture;
            isOwnTexture = true;
        }
#endif

        public void SetContent(string content)
        {
            if (Content == content)
            {
                return;
            }

            Content = content;
            var renderer = (RawImage)this.GetComponent(typeof(RawImage));

            if (isOwnTexture)
            {
                var old = renderer.texture;
                renderer.texture = null;
                Destroy(old);
            }

            var texture = Render(Content);
            renderer.texture = texture;
            isOwnTexture = true;
        }

        public const byte MARGIN = 4;
        // We'll use the low bit to indicate that the pixel is locked.
        const byte BLACK_SYSTEM = 0x01;
        const byte WHITE_SYSTEM = 0xff;
        const byte BLACK = 0x00;
        const byte WHITE = 0xfe;

#if !COMPILER_UDONSHARP
        public
#endif
        void MeasureData(string content, out byte version, out ushort contentLength, out ushort dataLength)
        {
            contentLength = MeasureUtf8(content);
            var encodedContentLength = RoundUpToQrLength(contentLength, out version);
            ushort contentLengthLength;
            if (version <= 9)
            {
                contentLengthLength = 1;
            }
            else
            {
                contentLengthLength = 2;
            }
            dataLength = (ushort)(contentLengthLength + encodedContentLength + 1);
        }

#if !COMPILER_UDONSHARP
        public
#endif
        Texture2D Render(string content)
        {
            MeasureData(content, out var version, out var contentLength, out var dataLength);
            var matrixSize = GetMatrixSize(version);
            var stride = (byte)(matrixSize + MARGIN * 2);
            GetErrorBlockSizes(version, out var aSize, out var aCount, out var bSize, out var bCount, out var ecLength);
            var interleavedLength = (ushort)(dataLength + (aCount + bCount) * ecLength);

            // Because matrixSize*matrixSize represents each *bit* of the data with extras, it's more
            // than enough space to fit other data while building the image.
            var buffer = new byte[matrixSize * matrixSize + stride * stride];

            Encode(content, version, buffer, interleavedLength, contentLength, dataLength);
            GenerateDataLayout(buffer, interleavedLength, aSize, aCount, bSize, bCount, ecLength, interleavedLength + dataLength, 0);
            var imageOffset = matrixSize * matrixSize;
            CreateEmptyImage(buffer, imageOffset, version, matrixSize);
            FillImage(buffer, 0, interleavedLength, imageOffset, matrixSize);

            byte bestMask = 0;
            uint bestScore = uint.MaxValue;
            for (byte i = 0; i < 8; i++)
            {
                Blit(buffer, imageOffset + stride * MARGIN + MARGIN, matrixSize, stride, 0, matrixSize);
                ApplyMask(buffer, i, 0, matrixSize, matrixSize);
                var score = EvaluateMask(buffer, 0, matrixSize, matrixSize);
                if (score < bestScore)
                {
                    bestMask = i;
                    bestScore = score;
                }
            }

            ApplyMask(buffer, bestMask, imageOffset + stride * MARGIN + MARGIN, matrixSize, stride);
            SetSystemBits(buffer, version, bestMask, imageOffset + stride * MARGIN + MARGIN, matrixSize, stride);

            FixLowBits(buffer, imageOffset, stride * stride);

            var texture = new Texture2D(stride, stride, TextureFormat.R8, true);
            // Someday maybe Udon will support this.
#if !COMPILER_UDONSHARP
            texture.SetPixelData(buffer, 0, imageOffset);
#else
            var pixels = new Color32[stride * stride];
            var colorCursor = 0;
            var end = stride * stride;
            var byteCursor = matrixSize * matrixSize;
            while (colorCursor < end)
            {
                pixels[colorCursor++] = new Color32(buffer[byteCursor++], 0, 0, 255);
            }
            texture.SetPixels32(pixels);
#endif
            texture.Apply(true);
            return texture;
        }

        void SetSystemBits(byte[] buffer, byte version, byte mask, int offset, byte size, byte stride)
        {
            var formatBits = GetFormatBits(mask);
            var cursor1 = offset + stride * 8;
            var cursor2 = offset + stride * (size - 1) + 8;
            if ((formatBits & 0x4000) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1++;
            cursor2 -= stride;
            if ((formatBits & 0x2000) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1++;
            cursor2 -= stride;
            if ((formatBits & 0x1000) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1++;
            cursor2 -= stride;
            if ((formatBits & 0x800) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1++;
            cursor2 -= stride;
            if ((formatBits & 0x400) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1++;
            cursor2 -= stride;
            if ((formatBits & 0x200) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 += 2;
            cursor2 -= stride;
            if ((formatBits & 0x100) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1++;
            cursor2 = offset + stride * 8 + size - 8;
            if ((formatBits & 0x80) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 -= stride;
            cursor2++;
            if ((formatBits & 0x40) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 -= stride * 2;
            cursor2++;
            if ((formatBits & 0x20) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 -= stride;
            cursor2++;
            if ((formatBits & 0x10) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 -= stride;
            cursor2++;
            if ((formatBits & 0x8) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 -= stride;
            cursor2++;
            if ((formatBits & 0x4) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 -= stride;
            cursor2++;
            if ((formatBits & 0x2) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }
            cursor1 -= stride;
            cursor2++;
            if ((formatBits & 0x1) == 0)
            {
                buffer[cursor1] = WHITE_SYSTEM;
                buffer[cursor2] = WHITE_SYSTEM;
            }

            var versionInfo = GetVersionInfo(version);
            if (versionInfo != 0)
            {
                var test = 1;
                cursor1 = offset + (size - 11) * stride;
                cursor2 = offset + size - 11;
                for (var i = 0; i < 6; i++)
                {
                    for (var j = 0; j < 3; j++)
                    {
                        if ((versionInfo & test) == 0)
                        {
                            buffer[cursor1] = WHITE_SYSTEM;
                            buffer[cursor2] = WHITE_SYSTEM;
                        }
                        test <<= 1;
                        cursor1 += stride;
                        cursor2 += 1;
                    }
                    cursor1 -= stride * 3 - 1;
                    cursor2 += stride - 3;
                }
            }
        }

#if !COMPILER_UDONSHARP
        public
#endif
        void Encode(string content, byte version, byte[] buffer, int offset, ushort contentLength, ushort dataLength)
        {
            var cursor = offset;
            buffer[cursor] = 0x40;
            if (version <= 9)
            {
                buffer[cursor++] |= (byte)((contentLength >> 4) & 0xff);
                buffer[cursor] = (byte)((contentLength << 4) & 0xff);
            }
            else
            {
                buffer[cursor++] |= (byte)(contentLength >> 12);
                buffer[cursor++] = (byte)((contentLength >> 4) & 0xff);
                buffer[cursor] = (byte)((contentLength << 4) & 0xf0);
            }

            for (int i = 0; i < content.Length; i++)
            {
                var c = (int)content[i];
                if (c < 0x80)
                {
                    // 0aaabbbb
                    // 0aaa bbbb
                    buffer[cursor++] |= (byte)(c >> 4);
                    buffer[cursor] = (byte)((c << 4) & 0xf0);
                    continue;
                }
                if (c < 0x800)
                {
                    // 00000abb ccddeeee
                    // 110abbcc 10ddeeee
                    // 110a bbcc10dd eeee 
                    buffer[cursor++] |= (byte)(0xc | (c >> 10));
                    buffer[cursor++] = (byte)(((c >> 2) & 0xf0) | 0x8 | ((c >> 4) & 0x3));
                    buffer[cursor] = (byte)((c << 4) & 0xf0);
                    continue;
                }
                if ((c & 0xfc00) == 0xd800 && i + 1 < content.Length)
                {
                    var n = content[i + 1];
                    if ((n & 0xfc00) == 0xdc00)
                    {
                        // This is a surrogate pair.
                        c = 0x10000 + (((c & 0x3ff) << 10) | (n & 0x3ff));
                        // 000aaabb ccccddee eeffgggg
                        // 11110aaa 10bbcccc 10ddeeee 10ffgggg
                        // 1111 0aaa10bb cccc10dd eeee10ff gggg
                        buffer[cursor++] |= 0xf;
                        buffer[cursor++] = (byte)(((c >> 14) & 0xf0) | 0x8 | ((c >> 16) & 0x3));
                        buffer[cursor++] = (byte)(((c >> 8) & 0xf0) | 0x8 | ((c >> 10) & 0x3));
                        buffer[cursor++] = (byte)(((c >> 2) & 0xf0) | 0x8 | ((c >> 4) & 0x3));
                        buffer[cursor] = (byte)((c << 4) & 0xf0);
                        continue;
                    }
                }
                // aaaabbcc ccddeeee
                // 1110aaaa 10bbcccc 10ddeeee
                // 1110 aaaa10bb cccc10dd eeee
                buffer[cursor++] |= 0xe;
                buffer[cursor++] = (byte)(((c >> 8) & 0xf0) | 0x8 | ((c >> 10) & 0x3));
                buffer[cursor++] = (byte)(((c >> 2) & 0xf0) | 0x8 | ((c >> 4) & 0x3));
                buffer[cursor] = (byte)((c << 4) & 0xf0);
                continue;
            }

            cursor++;
            for (int i = 0, j = offset + dataLength - cursor; j != 0; i = (i + 1) & 1, j--)
            {
                if (i == 0)
                {
                    buffer[cursor++] = 236;
                }
                else
                {
                    buffer[cursor++] = 17;
                }
            }
        }

        ushort MeasureUtf8(string content)
        {
            ushort contentLength = 0;
            for (var i = 0; i < content.Length; i++)
            {
                var c = content[i];
                if (c < 0x80)
                {
                    contentLength += 1;
                    continue;
                }
                if (c < 0x800)
                {
                    contentLength += 2;
                    continue;
                }
                if ((c & 0xfc00) == 0xd800 && i + 1 < content.Length)
                {
                    var n = content[i + 1];
                    if ((n & 0xfc00) == 0xdc)
                    {
                        // This is a surrogate pair.
                        contentLength += 4;
                        continue;
                    }
                }
                contentLength += 3;
            }
            return contentLength;
        }

        /// <summary>
        /// Create the interleaved data words and error correction words.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>
        /// <paramref name="aSize"/> * <paramref name="aCount"/> +
        /// <paramref name="bSize"/> * <paramref name="bCount"/>
        /// </c>
        /// bytes will be read from <paramref name="dataOffset"/>.
        /// </para>
        /// <para>
        /// <c>
        /// (<paramref name="aSize"/> + <paramref name="ecLength"/>) * <paramref name="aCount"/> +
        /// (<paramref name="bSize"/> + <paramref name="ecLength"/>) * <paramref name="bCount"/>
        /// </c>
        /// bytes will be written at <paramref name="outputOffset"/>.
        /// </para>
        /// <para>
        /// <c>
        /// Math.Max(<paramref name="aSize"/>, <paramref name="bSize"/>) + <paramref name="ecLength"/>)
        /// </c>
        /// bytes must be available at <paramref name="tempOffset"/>.
        /// </para>
        /// <para>
        /// <paramref name="aCount"/> must be less than <paramref name="bCount"/>.
        /// </para>
        /// </remarks>
#if !COMPILER_UDONSHARP
        public
#endif
        void GenerateDataLayout(byte[] buffer, int dataOffset, byte aSize, byte aCount, byte bSize, byte bCount, byte ecLength, int tempOffset, int outputOffset)
        {
            var totalCount = aCount + bCount;
            var polynomial = GetPolynomial(ecLength);
            var logs = GetLogs();
            var antilogs = GetAntilogs();

            var dataPos = dataOffset;
            for (int i = 0; i < aCount; i++)
            {
                for (int j = 0, k = outputOffset + i; j < aSize; j++, k += totalCount)
                {
                    buffer[k] = buffer[dataPos++];
                }
            }
            for (int i = 0; i < bCount; i++)
            {
                var j = 0;
                var k = outputOffset + aCount + i;
                for (; j < aSize; j++, k += totalCount)
                {
                    buffer[k] = buffer[dataPos++];
                }
                // The previous loop causes k to overshoot.
                k -= aCount;
                for (; j < bSize; j++, k += bCount)
                {
                    buffer[k] = buffer[dataPos++];
                }
            }

            var ecInterleavedOffset = outputOffset + aCount * aSize + bCount * bSize;
            dataPos = dataOffset;
            for (var i = 0; i < aCount; i++, dataPos += aSize)
            {
                Array.Clear(buffer, tempOffset + aSize, ecLength);
                Array.Copy(buffer, dataPos, buffer, tempOffset, aSize);
                GenerateErrorCorrectionBlock(buffer, tempOffset, aSize, polynomial, logs, antilogs);

                for (int j = 0, k = tempOffset + aSize, l = ecInterleavedOffset + i; j < ecLength; j++, k++, l += totalCount)
                {
                    buffer[l] = buffer[k];
                }
            }
            for (var i = 0; i < bCount; i++, dataPos += bSize)
            {
                Array.Clear(buffer, tempOffset + bSize, ecLength);
                Array.Copy(buffer, dataPos, buffer, tempOffset, bSize);
                GenerateErrorCorrectionBlock(buffer, tempOffset, bSize, polynomial, logs, antilogs);
                for (int j = 0, k = tempOffset + bSize, l = ecInterleavedOffset + (aCount + i); j < ecLength; j++, k++, l += totalCount)
                {
                    buffer[l] = buffer[k];
                }
            }
        }

#if !COMPILER_UDONSHARP
        public
#endif
        void GenerateErrorCorrectionBlock(byte[] buffer, int offset, byte dataLength, byte[] polynomial, byte[] logs, byte[] antilogs)
        {
            for (int i = 0, j = offset; i < dataLength; i++, j++)
            {
                var v = buffer[j];
                if (v == 0)
                {
                    continue;
                }
                var a = antilogs[v];
                for (int k = 0, l = j; k < polynomial.Length; k++, l++)
                {
                    buffer[l] ^= logs[((ushort)polynomial[k] + (ushort)a) % 255];
                }
            }
        }

#if !COMPILER_UDONSHARP
        public
#endif
        void CreateEmptyImage(byte[] buffer, int offset, byte version, byte matrixSize)
        {
            var size = MARGIN * 2 + matrixSize;

            // Fill margins.
            var cursor = offset;
            for (var x = 0; x < size; x++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            for (var y = 1; y < MARGIN; y++)
            {
                // No Array.Fill so we'll just copy from the first row.
                Array.Copy(buffer, offset, buffer, cursor, size);
                cursor += size;
            }
            for (var y = 0; y < matrixSize; y++)
            {
                Array.Copy(buffer, offset, buffer, cursor, MARGIN);
                cursor += MARGIN;
                // When you make a new byte[] in .Net, all the memory is zeroed.
                // However, we've been using this array and may have written temporary data into this
                // region. We'll make sure everything that isn't margin is zeroed again.
                Array.Clear(buffer, cursor, matrixSize);
                cursor += matrixSize;
                Array.Copy(buffer, offset, buffer, cursor, MARGIN);
                cursor += MARGIN;
            }
            for (var y = 0; y < MARGIN; y++)
            {
                Array.Copy(buffer, offset, buffer, cursor, size);
                cursor += size;
            }

            // Timing patterns.
            var timingLength = matrixSize - 16;
            cursor = offset + (6 + MARGIN) * size + MARGIN + 8;
            for (var i = 0; i < timingLength; i++, cursor++)
            {
                if (i % 2 == 0)
                {
                    buffer[cursor] = BLACK_SYSTEM;
                }
                else
                {
                    buffer[cursor] = WHITE_SYSTEM;
                }
            }
            cursor = offset + (8 + MARGIN) * size + MARGIN + 6;
            for (var i = 0; i < timingLength; i++, cursor += size)
            {
                if (i % 2 == 0)
                {
                    buffer[cursor] = BLACK_SYSTEM;
                }
                else
                {
                    buffer[cursor] = WHITE_SYSTEM;
                }
            }

            // Finder pattern 0,0.
            cursor = offset + MARGIN * size + MARGIN;
            for (var i = 0; i < 7; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            buffer[cursor++] = BLACK_SYSTEM;
            for (var i = 0; i < 5; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor++] = BLACK_SYSTEM;
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            for (var i = 0; i < 3; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
                buffer[cursor++] = WHITE_SYSTEM;
                for (var j = 0; j < 3; j++)
                {
                    buffer[cursor++] = BLACK_SYSTEM;
                }
                buffer[cursor++] = WHITE_SYSTEM;
                buffer[cursor++] = BLACK_SYSTEM;
                buffer[cursor++] = WHITE_SYSTEM;
                buffer[cursor] = BLACK_SYSTEM;
                cursor += size - 8;
            }
            buffer[cursor++] = BLACK_SYSTEM;
            for (var i = 0; i < 5; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor++] = BLACK_SYSTEM;
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            for (var i = 0; i < 7; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            for (var i = 0; i < 8; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            for (var i = 0; i < 9; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }

            // Finder pattern 1,0.
            cursor = offset + MARGIN * size + MARGIN + matrixSize - 8 - 3;
            if (version < 7)
            {
                cursor += 3;
            }
            else
            {
                for (var i = 0; i < 3; i++)
                {
                    buffer[cursor++] = BLACK_SYSTEM;
                }
            }
            buffer[cursor++] = WHITE_SYSTEM;
            for (var i = 0; i < 7; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }
            cursor += size - 8 - 3;
            if (version < 7)
            {
                cursor += 3;
            }
            else
            {
                for (var i = 0; i < 3; i++)
                {
                    buffer[cursor++] = BLACK_SYSTEM;
                }
            }
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor++] = BLACK_SYSTEM;
            for (var i = 0; i < 5; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 7 - 3;
            for (var i = 0; i < 3; i++)
            {
                if (version < 7)
                {
                    cursor += 3;
                }
                else
                {
                    for (var j = 0; j < 3; j++)
                    {
                        buffer[cursor++] = BLACK_SYSTEM;
                    }
                }
                buffer[cursor++] = WHITE_SYSTEM;
                buffer[cursor++] = BLACK_SYSTEM;
                buffer[cursor++] = WHITE_SYSTEM;
                for (var j = 0; j < 3; j++)
                {
                    buffer[cursor++] = BLACK_SYSTEM;
                }
                buffer[cursor++] = WHITE_SYSTEM;
                buffer[cursor] = BLACK_SYSTEM;
                cursor += size - 7 - 3;
            }
            if (version < 7)
            {
                cursor += 3;
            }
            else
            {
                for (var i = 0; i < 3; i++)
                {
                    buffer[cursor++] = BLACK_SYSTEM;
                }
            }
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor++] = BLACK_SYSTEM;
            for (var i = 0; i < 5; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 7;
            buffer[cursor++] = WHITE_SYSTEM;
            for (var i = 0; i < 7; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }
            cursor += size - 8;
            for (var i = 0; i < 8; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            cursor += size - 8;
            for (var i = 0; i < 8; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }

            // Finder pattern 0,1.
            cursor = offset + (MARGIN + matrixSize - 8 - 3) * size + MARGIN;
            if (version < 7)
            {
                cursor += size * 3;
            }
            else
            {
                for (var i = 0; i < 3; i++)
                {
                    for (var j = 0; j < 6; j++)
                    {
                        buffer[cursor++] = BLACK_SYSTEM;
                    }
                    cursor += size - 6;
                }
            }
            for (var i = 0; i < 8; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            for (var i = 0; i < 7; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            buffer[cursor++] = BLACK_SYSTEM;
            for (var i = 0; i < 5; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor++] = BLACK_SYSTEM;
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            for (var i = 0; i < 3; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
                buffer[cursor++] = WHITE_SYSTEM;
                for (var j = 0; j < 3; j++)
                {
                    buffer[cursor++] = BLACK_SYSTEM;
                }
                buffer[cursor++] = WHITE_SYSTEM;
                buffer[cursor++] = BLACK_SYSTEM;
                buffer[cursor++] = WHITE_SYSTEM;
                buffer[cursor] = BLACK_SYSTEM;
                cursor += size - 8;
            }
            buffer[cursor++] = BLACK_SYSTEM;
            for (var i = 0; i < 5; i++)
            {
                buffer[cursor++] = WHITE_SYSTEM;
            }
            buffer[cursor++] = BLACK_SYSTEM;
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;
            cursor += size - 8;
            for (var i = 0; i < 7; i++)
            {
                buffer[cursor++] = BLACK_SYSTEM;
            }
            buffer[cursor++] = WHITE_SYSTEM;
            buffer[cursor] = BLACK_SYSTEM;

            var alignmentPatternLocations = GetAlignmentPatternLocatons(version);
            for (var ay = 0; ay < alignmentPatternLocations.Length; ay++)
            {
                var cy = alignmentPatternLocations[ay];
                for (var ax = 0; ax < alignmentPatternLocations.Length; ax++)
                {
                    var cx = alignmentPatternLocations[ax];

                    // Finder 0,0.
                    if (cx < 10 && cy < 10)
                    {
                        continue;
                    }
                    // Finder 1,0.
                    if (cx > matrixSize - 9 && cy < 10)
                    {
                        continue;
                    }
                    // Finder 0,1.
                    if (cx < 10 && cy > matrixSize - 9)
                    {
                        continue;
                    }

                    cursor = offset + (MARGIN + cy - 2) * size + MARGIN + cx - 2;
                    for (var i = 0; i < 5; i++)
                    {
                        buffer[cursor++] = BLACK_SYSTEM;
                    }
                    cursor += size - 5;
                    buffer[cursor++] = BLACK_SYSTEM;
                    for (var i = 0; i < 3; i++)
                    {
                        buffer[cursor++] = WHITE_SYSTEM;
                    }
                    buffer[cursor] = BLACK_SYSTEM;
                    cursor += size - 4;
                    buffer[cursor++] = BLACK_SYSTEM;
                    buffer[cursor++] = WHITE_SYSTEM;
                    buffer[cursor++] = BLACK_SYSTEM;
                    buffer[cursor++] = WHITE_SYSTEM;
                    buffer[cursor] = BLACK_SYSTEM;
                    cursor += size - 4;
                    buffer[cursor++] = BLACK_SYSTEM;
                    for (var i = 0; i < 3; i++)
                    {
                        buffer[cursor++] = WHITE_SYSTEM;
                    }
                    buffer[cursor] = BLACK_SYSTEM;
                    cursor += size - 4;
                    for (var i = 0; i < 5; i++)
                    {
                        buffer[cursor++] = BLACK_SYSTEM;
                    }
                }
            }
        }

        void FillImage(byte[] buffer, int dataOffset, ushort dataLength, int imageOffset, byte matrixSize)
        {
            var size = MARGIN * 2 + matrixSize;
            var cursor = imageOffset + (MARGIN + matrixSize) * size + MARGIN + matrixSize - 2;
            var x = matrixSize - 2;
            var y = matrixSize;
            var s = false;
            var u = true;
            byte word = buffer[dataOffset];
            ushort i = dataLength;
            byte j = 0;
            while (true)
            {
                do
                {
                    if (s)
                    {
                        cursor -= 1;
                        x -= 1;
                        s = false;
                    }
                    else
                    {
                        if (u)
                        {
                            if (y == 0)
                            {
                                u = false;
                                x -= 1;
                                cursor -= 1;
                                // Skip vertical timing pattern.
                                if (x == 6)
                                {
                                    x -= 1;
                                    cursor -= 1;
                                }
                            }
                            else
                            {
                                y -= 1;
                                x += 1;
                                cursor -= size - 1;
                            }
                        }
                        else
                        {
                            if (y + 1 == matrixSize)
                            {
                                u = true;
                                x -= 1;
                                cursor -= 1;
                                // Skip vertical timing pattern.
                                if (x == 6)
                                {
                                    x -= 1;
                                    cursor -= 1;
                                }
                            }
                            else
                            {
                                y += 1;
                                x += 1;
                                cursor += size + 1;
                            }
                        }
                        s = true;
                    }
                }
                while ((buffer[cursor] & 1) != 0);

                if ((word & 0x80) == 0)
                {
                    buffer[cursor] = WHITE;
                }

                if (j < 7)
                {
                    j++;
                    word = (byte)((word << 1) & 0xff);
                }
                else
                {
                    if (--i == 0)
                    {
                        break;
                    }
                    j = 0;
                    word = buffer[++dataOffset];
                }
            }
        }

        void Blit(byte[] buffer, int srcOffset, byte size, byte srcStride, int dstOffset, byte dstStride)
        {
            for (int y = 0, srcRow = srcOffset, dstRow = dstOffset; y < size; y++, srcRow += srcStride, dstRow += dstStride)
            {
                Array.Copy(buffer, srcRow, buffer, dstRow, size);
            }
        }

#if !COMPILER_UDONSHARP
        public
#endif
        void ApplyMask(byte[] buffer, byte mask, int offset, byte matrixSize, byte stride)
        {
            int i, row;
            ushort x, y;
            byte v, j;
            switch (mask)
            {
                case 0:
                    for (y = 0, row = offset; y < matrixSize; y++, row += stride)
                    {
                        for (x = (ushort)(y % 2), i = row + x; x < matrixSize; x += 2, i += 2)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                    }
                    break;
                case 1:
                    for (y = 0, row = offset; y < matrixSize; y += 2, row += stride * 2)
                    {
                        for (x = 0, i = row; x < matrixSize; x++, i++)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                    }
                    break;
                case 2:
                    for (y = 0, row = offset; y < matrixSize; y++, row += stride)
                    {
                        for (x = 0, i = row; x < matrixSize; x += 3, i += 3)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                    }
                    break;
                case 3:
                    for (y = 0, row = offset; y < matrixSize; y++, row += stride)
                    {
                        for (x = (ushort)(2 - ((y + 2) % 3)), i = row + x; x < matrixSize; x += 3, i += 3)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                    }
                    break;
                case 4:
                    for (y = 0, row = offset; y < matrixSize; y++, row += stride)
                    {
                        for (x = (ushort)(((y / 2) % 2) * 3), i = row + x; x < matrixSize;)
                        {
                            for (j = 0; j < 3 && x < matrixSize; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x += 3;
                            i += 3;
                        }
                    }
                    break;
                case 5:
                    for (y = 0, row = offset; y < matrixSize; y++, row += stride)
                    {
                        // Row 0: solid line
                        for (x = 0, i = row; x < matrixSize; x++, i++)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 1: x % 6 == 0
                        for (x = 0, i = row; x < matrixSize; x += 6, i += 6)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 2: x % 3 == 0
                        for (x = 0, i = row; x < matrixSize; x += 3, i += 3)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 3: x % 2 == 0
                        for (x = 0, i = row; x < matrixSize; x += 2, i += 2)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 4: x % 3 == 0
                        for (x = 0, i = row; x < matrixSize; x += 3, i += 3)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 5: x % 6 == 0
                        for (x = 0, i = row; x < matrixSize; x += 6, i += 6)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                    }
                    break;
                case 6:
                    for (y = 0, row = offset; y < matrixSize; y++, row += stride)
                    {
                        // Row 0: solid line
                        for (x = 0, i = row; x < matrixSize; x++, i++)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 1: (x / 3) % 2 == 0
                        for (x = 0, i = row; x < matrixSize;)
                        {
                            for (j = 0; x < matrixSize && j < 3; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x += 3;
                            i += 3;
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 2: (x % 3) / 2 == 0
                        for (x = 0, i = row; x < matrixSize;)
                        {
                            for (j = 0; x < matrixSize && j < 2; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x++;
                            i++;
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 3: x % 2 == 0
                        for (x = 0, i = row; x < matrixSize; x += 2, i += 2)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 4: ((x + 1) % 3) / 2 == 0
                        for (x = 0, i = row; x < matrixSize;)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                            x += 2;
                            i += 2;
                            for (j = 0; x < matrixSize && j < 2; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x++;
                            i++;
                            if (x >= matrixSize)
                            {
                                break;
                            }
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                            x++;
                            i++;
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 5: ((x + 2) % 6) / 3 == 0
                        for (x = 0, i = row; x < matrixSize;)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                            x += 4;
                            i += 4;
                            for (j = 0; x < matrixSize && j < 2; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                        }
                    }
                    break;
                case 7:
                    for (y = 0, row = offset; y < matrixSize; y++, row += stride)
                    {
                        // Row 0: (mask 1 skip 1)*
                        for (x = 0, i = row; x < matrixSize; x += 2, i += 2)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 1: (skip 3 mask 3)*
                        for (x = 3, i = row + 3; x < matrixSize;)
                        {
                            for (j = 0; x < matrixSize && j < 3; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x += 3;
                            i += 3;
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 2: mask 1 (skip 3 mask 3)*
                        v = buffer[row];
                        if ((v & 0x1) != 1)
                        {
                            buffer[row] = (byte)(v ^ 0xfe);
                        }
                        for (x = 4, i = row + 4; x < matrixSize;)
                        {
                            for (j = 0; x < matrixSize && j < 3; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x += 3;
                            i += 3;
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 3: (skip 1 mask 1)*
                        for (x = 1, i = row + 1; x < matrixSize; x += 2, i += 2)
                        {
                            v = buffer[i];
                            if ((v & 0x1) != 1)
                            {
                                buffer[i] = (byte)(v ^ 0xfe);
                            }
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 4: (mask 3 skip 3)*
                        for (x = 0, i = row; x < matrixSize;)
                        {
                            for (j = 0; x < matrixSize && j < 3; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x += 3;
                            i += 3;
                        }
                        y++;
                        if (y == matrixSize)
                        {
                            break;
                        }
                        row += stride;
                        // Row 5: skip 1 (mask 3 skip 3)*
                        for (x = 1, i = row + 1; x < matrixSize;)
                        {
                            for (j = 0; x < matrixSize && j < 3; x++, i++, j++)
                            {
                                v = buffer[i];
                                if ((v & 0x1) != 1)
                                {
                                    buffer[i] = (byte)(v ^ 0xfe);
                                }
                            }
                            x += 3;
                            i += 3;
                        }
                    }
                    break;
            }
        }

        uint EvaluateMask(byte[] buffer, int offset, byte size, byte stride)
        {
            uint score = 0;

            // Horizontal repeated bits.
            for (int y = 0, row = offset; y < size; y++, row += stride)
            {
                var last = (buffer[row] & 0x80) != 0;
                byte run = 1;
                for (int x = 1, i = row + 1; x < size; x++, i++)
                {
                    var value = (buffer[i] * 0x80) != 0;
                    if (value == last)
                    {
                        run++;
                        if (run == 5)
                        {
                            score += 3;
                        }
                        else if (run > 5)
                        {
                            score++;
                        }
                    }
                    else
                    {
                        last = value;
                        run = 1;
                    }
                }
            }

            // Vertical repeated bits.
            for (int x = 0, column = offset; x < size; x++, column++)
            {
                var last = (buffer[column] & 0x80) != 0;
                byte run = 1;
                for (int y = 1, i = column + stride; y < size; y++, i += stride)
                {
                    var value = (buffer[i] * 0x80) != 0;
                    if (value == last)
                    {
                        run++;
                        if (run == 5)
                        {
                            score += 3;
                        }
                        else if (run >= 5)
                        {
                            score++;
                        }
                    }
                    else
                    {
                        last = value;
                        run = 1;
                    }
                }
            }

            // 2x2 blocks.
            for (int y = 0, row = offset; y < size - 1; y++, row += stride)
            {
                bool topLeft = (buffer[row] & 0x80) != 0;
                bool bottomLeft = (buffer[row + stride] & 0x80) != 0;
                for (int x = 1, i = row + 1; x < size; x++, i++)
                {
                    bool topRight = (buffer[i] & 0x80) != 0;
                    bool bottomRight = (buffer[i + stride] & 0x80) != 0;
                    if (topLeft == bottomLeft && topLeft == topRight && topLeft == bottomRight)
                    {
                        score += 3;
                    }
                    topLeft = topRight;
                    bottomLeft = bottomRight;
                }
            }

            // Horizontal finder+margin patterns.
            for (int y = 0, row = offset; y < size; y++, row += stride)
            {
                int x = 0, i = row;
                ushort window = 0;
                for (; x < 10; x++, i++)
                {
                    window = (ushort)((window << 1) | (buffer[i] >> 7));
                }
                for (; x < size; x++, i++)
                {
                    window = (ushort)(((window << 1) | (buffer[i] >> 7)) & 0x7ff);
                    if (window == 0x22f || window == 0x7a2)
                    {
                        score += 40;
                    }
                }
            }

            // Vertical finder+margin patterns.
            for (int x = 0, column = offset; x < size; x++, column++)
            {
                int y = 0, i = column;
                ushort window = 0;
                for (; y < 10; y++, i += stride)
                {
                    window = (ushort)((window << 1) | (buffer[i] >> 7));
                }
                for (; y < size; y++, i += stride)
                {
                    window = (ushort)(((window << 1) | (buffer[i] >> 7)) & 0x7ff);
                    if (window == 0x22f || window == 0x7a2)
                    {
                        score += 40;
                    }
                }
            }

            // Ratio.
            ushort darkCount = 0;
            for (int y = 0, row = offset; y < size; y++, row += stride)
            {
                for (int x = 0, i = row; x < size; x++, i++)
                {
                    if ((buffer[i] & 0x80) == 0)
                    {
                        darkCount++;
                    }
                }
            }
            byte percent = (byte)((darkCount * 100) / (size * size));
            if (percent < 50)
            {
                score += (uint)(((50 - percent) / 5) * 10);
            }
            else if (50 < percent)
            {
                score += (uint)(((percent - 50) / 5) * 10);
            }

            return score;
        }

        void FixLowBits(byte[] buffer, int offset, int size)
        {
            for (; size != 0; offset++, size--)
            {
                var v = buffer[offset];
                buffer[offset] = (byte)((v & 0xfe) | ((v & 0x2) >> 7));
            }
        }

        ushort GetFormatBits(byte mask)
        {
            switch (mask)
            {
                case 0:
                    return 0x77c4;
                case 1:
                    return 0x72f3;
                case 2:
                    return 0x7daa;
                case 3:
                    return 0x789d;
                case 4:
                    return 0x6a2f;
                case 5:
                    return 0x6318;
                case 6:
                    return 0x6c41;
                case 7:
                    return 0x6976;
                default:
                    return 0;
            }
        }

        uint GetVersionInfo(byte version)
        {
            switch (version)
            {
                case 7:
                    return 0x07c94;
                case 8:
                    return 0x085bc;
                case 9:
                    return 0x09a99;
                case 10:
                    return 0x0a4d3;
                case 11:
                    return 0x0bbf6;
                case 12:
                    return 0x0c762;
                case 13:
                    return 0x0d847;
                case 14:
                    return 0x0e60d;
                case 15:
                    return 0x0f928;
                case 16:
                    return 0x10b78;
                case 17:
                    return 0x1145d;
                case 18:
                    return 0x12a17;
                case 19:
                    return 0x13532;
                case 20:
                    return 0x149a6;
                case 21:
                    return 0x15683;
                case 22:
                    return 0x168c9;
                case 23:
                    return 0x177ec;
                case 24:
                    return 0x18ec4;
                case 25:
                    return 0x191e1;
                case 26:
                    return 0x1afab;
                case 27:
                    return 0x1b08e;
                case 28:
                    return 0x1cc1a;
                case 29:
                    return 0x1d33f;
                case 30:
                    return 0x1ed75;
                case 31:
                    return 0x1f250;
                case 32:
                    return 0x209d5;
                case 33:
                    return 0x216f0;
                case 34:
                    return 0x228ba;
                case 35:
                    return 0x2379f;
                case 36:
                    return 0x24b0b;
                case 37:
                    return 0x2542e;
                case 38:
                    return 0x26a64;
                case 39:
                    return 0x27541;
                case 40:
                    return 0x28c69;
                default:
                    return 0;
            }
        }

        byte[] GetAlignmentPatternLocatons(byte version)
        {
            switch (version)
            {
                case 1:
                    return new byte[0];
                case 2:
                    return new byte[] { 6, 18 };
                case 3:
                    return new byte[] { 6, 22 };
                case 4:
                    return new byte[] { 6, 26 };
                case 5:
                    return new byte[] { 6, 30 };
                case 6:
                    return new byte[] { 6, 34 };
                case 7:
                    return new byte[] { 6, 22, 38 };
                case 8:
                    return new byte[] { 6, 24, 42 };
                case 9:
                    return new byte[] { 6, 26, 46 };
                case 10:
                    return new byte[] { 6, 28, 50 };
                case 11:
                    return new byte[] { 6, 30, 54 };
                case 12:
                    return new byte[] { 6, 32, 58 };
                case 13:
                    return new byte[] { 6, 34, 62 };
                case 14:
                    return new byte[] { 6, 26, 46, 66 };
                case 15:
                    return new byte[] { 6, 26, 48, 70 };
                case 16:
                    return new byte[] { 6, 26, 50, 74 };
                case 17:
                    return new byte[] { 6, 30, 54, 78 };
                case 18:
                    return new byte[] { 6, 30, 56, 82 };
                case 19:
                    return new byte[] { 6, 30, 58, 86 };
                case 20:
                    return new byte[] { 6, 34, 62, 90 };
                case 21:
                    return new byte[] { 6, 28, 50, 72, 94 };
                case 22:
                    return new byte[] { 6, 26, 50, 74, 98 };
                case 23:
                    return new byte[] { 6, 30, 54, 78, 102 };
                case 24:
                    return new byte[] { 6, 28, 54, 80, 106 };
                case 25:
                    return new byte[] { 6, 32, 58, 84, 110 };
                case 26:
                    return new byte[] { 6, 30, 58, 86, 114 };
                case 27:
                    return new byte[] { 6, 34, 62, 90, 118 };
                case 28:
                    return new byte[] { 6, 26, 50, 74, 98, 122 };
                case 29:
                    return new byte[] { 6, 30, 54, 78, 102, 126 };
                case 30:
                    return new byte[] { 6, 26, 52, 78, 104, 130 };
                case 31:
                    return new byte[] { 6, 30, 56, 82, 108, 134 };
                case 32:
                    return new byte[] { 6, 34, 60, 86, 112, 138 };
                case 33:
                    return new byte[] { 6, 30, 58, 86, 114, 142 };
                case 34:
                    return new byte[] { 6, 34, 62, 90, 118, 146 };
                case 35:
                    return new byte[] { 6, 30, 54, 78, 102, 126, 150 };
                case 36:
                    return new byte[] { 6, 24, 50, 76, 102, 128, 154 };
                case 37:
                    return new byte[] { 6, 28, 54, 80, 106, 132, 158 };
                case 38:
                    return new byte[] { 6, 32, 58, 84, 110, 136, 162 };
                case 39:
                    return new byte[] { 6, 26, 54, 82, 110, 138, 166 };
                case 40:
                    return new byte[] { 6, 30, 58, 86, 114, 142, 170 };
                default:
                    return null;
            }
        }

#if !COMPILER_UDONSHARP
        public
#endif
        byte[] GetLogs()
        {
            // U# requires strings to be valid Unicode because it converts them to UTF-8.
            const string LOGS = "\u0001\u0002\u0004\u0008\u0010\u0020\u0040\u0080\u001d\u003a\u0074\u00e8\u00cd\u0087\u0013\u0026\u004c\u0098\u002d\u005a\u00b4\u0075\u00ea\u00c9\u008f\u0003\u0006\u000c\u0018\u0030\u0060\u00c0\u009d\u0027\u004e\u009c\u0025\u004a\u0094\u0035\u006a\u00d4\u00b5\u0077\u00ee\u00c1\u009f\u0023\u0046\u008c\u0005\u000a\u0014\u0028\u0050\u00a0\u005d\u00ba\u0069\u00d2\u00b9\u006f\u00de\u00a1\u005f\u00be\u0061\u00c2\u0099\u002f\u005e\u00bc\u0065\u00ca\u0089\u000f\u001e\u003c\u0078\u00f0\u00fd\u00e7\u00d3\u00bb\u006b\u00d6\u00b1\u007f\u00fe\u00e1\u00df\u00a3\u005b\u00b6\u0071\u00e2\u00d9\u00af\u0043\u0086\u0011\u0022\u0044\u0088\u000d\u001a\u0034\u0068\u00d0\u00bd\u0067\u00ce\u0081\u001f\u003e\u007c\u00f8\u00ed\u00c7\u0093\u003b\u0076\u00ec\u00c5\u0097\u0033\u0066\u00cc\u0085\u0017\u002e\u005c\u00b8\u006d\u00da\u00a9\u004f\u009e\u0021\u0042\u0084\u0015\u002a\u0054\u00a8\u004d\u009a\u0029\u0052\u00a4\u0055\u00aa\u0049\u0092\u0039\u0072\u00e4\u00d5\u00b7\u0073\u00e6\u00d1\u00bf\u0063\u00c6\u0091\u003f\u007e\u00fc\u00e5\u00d7\u00b3\u007b\u00f6\u00f1\u00ff\u00e3\u00db\u00ab\u004b\u0096\u0031\u0062\u00c4\u0095\u0037\u006e\u00dc\u00a5\u0057\u00ae\u0041\u0082\u0019\u0032\u0064\u00c8\u008d\u0007\u000e\u001c\u0038\u0070\u00e0\u00dd\u00a7\u0053\u00a6\u0051\u00a2\u0059\u00b2\u0079\u00f2\u00f9\u00ef\u00c3\u009b\u002b\u0056\u00ac\u0045\u008a\u0009\u0012\u0024\u0048\u0090\u003d\u007a\u00f4\u00f5\u00f7\u00f3\u00fb\u00eb\u00cb\u008b\u000b\u0016\u002c\u0058\u00b0\u007d\u00fa\u00e9\u00cf\u0083\u001b\u0036\u006c\u00d8\u00ad\u0047\u008e\u0001";
            var logs = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                logs[i] = (byte)LOGS[i];
            }
            return logs;
        }

#if !COMPILER_UDONSHARP
        public
#endif
        byte[] GetAntilogs()
        {
            const string ANTILOGS = "\u00af\u0000\u0001\u0019\u0002\u0032\u001a\u00c6\u0003\u00df\u0033\u00ee\u001b\u0068\u00c7\u004b\u0004\u0064\u00e0\u000e\u0034\u008d\u00ef\u0081\u001c\u00c1\u0069\u00f8\u00c8\u0008\u004c\u0071\u0005\u008a\u0065\u002f\u00e1\u0024\u000f\u0021\u0035\u0093\u008e\u00da\u00f0\u0012\u0082\u0045\u001d\u00b5\u00c2\u007d\u006a\u0027\u00f9\u00b9\u00c9\u009a\u0009\u0078\u004d\u00e4\u0072\u00a6\u0006\u00bf\u008b\u0062\u0066\u00dd\u0030\u00fd\u00e2\u0098\u0025\u00b3\u0010\u0091\u0022\u0088\u0036\u00d0\u0094\u00ce\u008f\u0096\u00db\u00bd\u00f1\u00d2\u0013\u005c\u0083\u0038\u0046\u0040\u001e\u0042\u00b6\u00a3\u00c3\u0048\u007e\u006e\u006b\u003a\u0028\u0054\u00fa\u0085\u00ba\u003d\u00ca\u005e\u009b\u009f\u000a\u0015\u0079\u002b\u004e\u00d4\u00e5\u00ac\u0073\u00f3\u00a7\u0057\u0007\u0070\u00c0\u00f7\u008c\u0080\u0063\u000d\u0067\u004a\u00de\u00ed\u0031\u00c5\u00fe\u0018\u00e3\u00a5\u0099\u0077\u0026\u00b8\u00b4\u007c\u0011\u0044\u0092\u00d9\u0023\u0020\u0089\u002e\u0037\u003f\u00d1\u005b\u0095\u00bc\u00cf\u00cd\u0090\u0087\u0097\u00b2\u00dc\u00fc\u00be\u0061\u00f2\u0056\u00d3\u00ab\u0014\u002a\u005d\u009e\u0084\u003c\u0039\u0053\u0047\u006d\u0041\u00a2\u001f\u002d\u0043\u00d8\u00b7\u007b\u00a4\u0076\u00c4\u0017\u0049\u00ec\u007f\u000c\u006f\u00f6\u006c\u00a1\u003b\u0052\u0029\u009d\u0055\u00aa\u00fb\u0060\u0086\u00b1\u00bb\u00cc\u003e\u005a\u00cb\u0059\u005f\u00b0\u009c\u00a9\u00a0\u0051\u000b\u00f5\u0016\u00eb\u007a\u0075\u002c\u00d7\u004f\u00ae\u00d5\u00e9\u00e6\u00e7\u00ad\u00e8\u0074\u00d6\u00f4\u00ea\u00a8\u0050\u0058\u00af";
            var antilogs = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                antilogs[i] = (byte)ANTILOGS[i];
            }
            return antilogs;
        }

#if !COMPILER_UDONSHARP
        public
#endif
        byte[] GetPolynomial(byte ecLength)
        {
            string polynomialStr;
            switch (ecLength)
            {
                case 7:
                    polynomialStr = "\u0057\u00e5\u0092\u0095\u00ee\u0066\u0015\u0000";
                    break;
                case 10:
                    polynomialStr = "\u00fb\u0043\u002e\u003d\u0076\u0046\u0040\u005e\u0020\u002d";
                    break;
                case 15:
                    polynomialStr = "\u0008\u00b7\u003d\u005b\u00ca\u0025\u0033\u003a\u003a\u00ed\u008c\u007c\u0005\u0063\u0069\u0000";
                    break;
                case 18:
                    polynomialStr = "\u00d7\u00ea\u009e\u005e\u00b8\u0061\u0076\u00aa\u004f\u00bb\u0098\u0094\u00fc\u00b3\u0005\u0062\u0060\u0099";
                    break;
                case 20:
                    polynomialStr = "\u0011\u003c\u004f\u0032\u003d\u00a3\u001a\u00bb\u00ca\u00b4\u00dd\u00e1\u0053\u00ef\u009c\u00a4\u00d4\u00d4\u00bc\u00be";
                    break;
                case 22:
                    polynomialStr = "\u00d2\u00ab\u00f7\u00f2\u005d\u00e6\u000e\u006d\u00dd\u0035\u00c8\u004a\u0008\u00ac\u0062\u0050\u00db\u0086\u00a0\u0069\u00a5\u00e7";
                    break;
                case 24:
                    polynomialStr = "\u00e5\u0079\u0087\u0030\u00d3\u0075\u00fb\u007e\u009f\u00b4\u00a9\u0098\u00c0\u00e2\u00e4\u00da\u006f\u0000\u0075\u00e8\u0057\u0060\u00e3\u0015";
                    break;
                case 26:
                    polynomialStr = "\u00ad\u007d\u009e\u0002\u0067\u00b6\u0076\u0011\u0091\u00c9\u006f\u001c\u00a5\u0035\u00a1\u0015\u00f5\u008e\u000d\u0066\u0030\u00e3\u0099\u0091\u00da\u0046";
                    break;
                case 28:
                    polynomialStr = "\u00a8\u00df\u00c8\u0068\u00e0\u00ea\u006c\u00b4\u006e\u00be\u00c3\u0093\u00cd\u001b\u00e8\u00c9\u0015\u002b\u00f5\u0057\u002a\u00c3\u00d4\u0077\u00f2\u0025\u0009\u007b";
                    break;
                case 30:
                    polynomialStr = "\u0029\u00ad\u0091\u0098\u00d8\u001f\u00b3\u00b6\u0032\u0030\u006e\u0056\u00ef\u0060\u00de\u007d\u002a\u00ad\u00e2\u00c1\u00e0\u0082\u009c\u0025\u00fb\u00d8\u00ee\u0028\u00c0\u00b4";
                    break;
                default:
                    polynomialStr = "";
                    break;
            }
            var polynomial = new byte[ecLength + 1];
            for (int i = 1, j = 0; i <= ecLength; i++, j++)
            {
                polynomial[i] = (byte)polynomialStr[j];
            }
            return polynomial;
        }

#if !COMPILER_UDONSHARP
        public
#endif
        ushort RoundUpToQrLength(ushort contentLength, out byte version)
        {
            if (contentLength <= 586)
            {
                if (contentLength <= 192)
                {
                    if (contentLength <= 78)
                    {
                        if (contentLength <= 32)
                        {
                            if (contentLength <= 17)
                            {
                                version = 1;
                                return 17;
                            }
                            version = 2;
                            return 32;
                        }
                        if (contentLength <= 53)
                        {
                            version = 3;
                            return 53;
                        }
                        version = 4;
                        return 78;
                    }
                    if (contentLength <= 134)
                    {
                        if (contentLength <= 106)
                        {
                            version = 5;
                            return 106;
                        }
                        version = 6;
                        return 134;
                    }
                    if (contentLength <= 154)
                    {
                        version = 7;
                        return 154;
                    }
                    version = 8;
                    return 192;
                }
                if (contentLength <= 367)
                {
                    if (contentLength <= 271)
                    {
                        if (contentLength <= 230)
                        {
                            version = 9;
                            return 230;
                        }
                        version = 10;
                        return 271;
                    }
                    if (contentLength <= 321)
                    {
                        version = 11;
                        return 321;
                    }
                    version = 12;
                    return 367;
                }
                if (contentLength <= 458)
                {
                    if (contentLength <= 425)
                    {
                        version = 13;
                        return 425;
                    }
                    version = 14;
                    return 458;
                }
                if (contentLength <= 520)
                {
                    version = 15;
                    return 520;
                }
                version = 16;
                return 586;
            }
            if (contentLength <= 1952)
            {
                if (contentLength <= 1171)
                {
                    if (contentLength <= 858)
                    {
                        if (contentLength <= 718)
                        {
                            if (contentLength <= 644)
                            {
                                version = 17;
                                return 644;
                            }
                            version = 18;
                            return 718;
                        }
                        if (contentLength <= 792)
                        {
                            version = 19;
                            return 792;
                        }
                        version = 20;
                        return 858;
                    }
                    if (contentLength <= 1003)
                    {
                        if (contentLength <= 929)
                        {
                            version = 21;
                            return 929;
                        }
                        version = 22;
                        return 1003;
                    }
                    if (contentLength <= 1091)
                    {
                        version = 23;
                        return 1091;
                    }
                    version = 24;
                    return 1171;
                }
                if (contentLength <= 1528)
                {
                    if (contentLength <= 1367)
                    {
                        if (contentLength <= 1273)
                        {
                            version = 25;
                            return 1273;
                        }
                        version = 26;
                        return 1367;
                    }
                    if (contentLength <= 1465)
                    {
                        version = 27;
                        return 1465;
                    }
                    version = 28;
                    return 1528;
                }
                if (contentLength <= 1732)
                {
                    if (contentLength <= 1628)
                    {
                        version = 29;
                        return 1628;
                    }
                    version = 30;
                    return 1732;
                }
                if (contentLength <= 1840)
                {
                    version = 31;
                    return 1840;
                }
                version = 32;
                return 1952;
            }
            if (contentLength <= 2431)
            {
                if (contentLength <= 2188)
                {
                    if (contentLength <= 2068)
                    {
                        version = 33;
                        return 2068;
                    }
                    version = 34;
                    return 2188;
                }
                if (contentLength <= 2303)
                {
                    version = 35;
                    return 2303;
                }
                version = 36;
                return 2431;
            }
            if (contentLength <= 2699)
            {
                if (contentLength <= 2563)
                {
                    version = 37;
                    return 2563;
                }
                version = 38;
                return 2699;
            }
            if (contentLength <= 2809)
            {
                version = 39;
                return 2809;
            }
            version = 40;
            return 2953;
        }

#if !COMPILER_UDONSHARP
        public
#endif
        void GetErrorBlockSizes(byte version, out byte aDataSize, out byte aDataCount, out byte bDataSize, out byte bDataCount, out byte ecLength)
        {
            switch (version) {
                case 1:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 19;
                    bDataCount = 1;
                    ecLength = 7;
                    break;
                case 2:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 34;
                    bDataCount = 1;
                    ecLength = 10;
                    break;
                case 3:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 55;
                    bDataCount = 1;
                    ecLength = 15;
                    break;
                case 4:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 80;
                    bDataCount = 1;
                    ecLength = 20;
                    break;
                case 5:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 108;
                    bDataCount = 1;
                    ecLength = 26;
                    break;
                case 6:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 68;
                    bDataCount = 2;
                    ecLength = 18;
                    break;
                case 7:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 78;
                    bDataCount = 2;
                    ecLength = 20;
                    break;
                case 8:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 97;
                    bDataCount = 2;
                    ecLength = 24;
                    break;
                case 9:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 116;
                    bDataCount = 2;
                    ecLength = 30;
                    break;
                case 10:
                    aDataSize = 68;
                    aDataCount = 2;
                    bDataSize = 69;
                    bDataCount = 2;
                    ecLength = 18;
                    break;
                case 11:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 81;
                    bDataCount = 4;
                    ecLength = 20;
                    break;
                case 12:
                    aDataSize = 92;
                    aDataCount = 2;
                    bDataSize = 93;
                    bDataCount = 2;
                    ecLength = 24;
                    break;
                case 13:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 107;
                    bDataCount = 4;
                    ecLength = 26;
                    break;
                case 14:
                    aDataSize = 115;
                    aDataCount = 3;
                    bDataSize = 116;
                    bDataCount = 1;
                    ecLength = 30;
                    break;
                case 15:
                    aDataSize = 87;
                    aDataCount = 5;
                    bDataSize = 88;
                    bDataCount = 1;
                    ecLength = 22;
                    break;
                case 16:
                    aDataSize = 98;
                    aDataCount = 5;
                    bDataSize = 99;
                    bDataCount = 1;
                    ecLength = 24;
                    break;
                case 17:
                    aDataSize = 107;
                    aDataCount = 1;
                    bDataSize = 108;
                    bDataCount = 5;
                    ecLength = 28;
                    break;
                case 18:
                    aDataSize = 120;
                    aDataCount = 5;
                    bDataSize = 121;
                    bDataCount = 1;
                    ecLength = 30;
                    break;
                case 19:
                    aDataSize = 113;
                    aDataCount = 3;
                    bDataSize = 114;
                    bDataCount = 4;
                    ecLength = 28;
                    break;
                case 20:
                    aDataSize = 107;
                    aDataCount = 3;
                    bDataSize = 108;
                    bDataCount = 5;
                    ecLength = 28;
                    break;
                case 21:
                    aDataSize = 116;
                    aDataCount = 4;
                    bDataSize = 117;
                    bDataCount = 4;
                    ecLength = 28;
                    break;
                case 22:
                    aDataSize = 111;
                    aDataCount = 2;
                    bDataSize = 112;
                    bDataCount = 7;
                    ecLength = 28;
                    break;
                case 23:
                    aDataSize = 121;
                    aDataCount = 4;
                    bDataSize = 122;
                    bDataCount = 5;
                    ecLength = 30;
                    break;
                case 24:
                    aDataSize = 117;
                    aDataCount = 6;
                    bDataSize = 118;
                    bDataCount = 4;
                    ecLength = 30;
                    break;
                case 25:
                    aDataSize = 106;
                    aDataCount = 8;
                    bDataSize = 107;
                    bDataCount = 4;
                    ecLength = 26;
                    break;
                case 26:
                    aDataSize = 114;
                    aDataCount = 10;
                    bDataSize = 115;
                    bDataCount = 2;
                    ecLength = 28;
                    break;
                case 27:
                    aDataSize = 122;
                    aDataCount = 8;
                    bDataSize = 123;
                    bDataCount = 4;
                    ecLength = 30;
                    break;
                case 28:
                    aDataSize = 117;
                    aDataCount = 3;
                    bDataSize = 118;
                    bDataCount = 10;
                    ecLength = 30;
                    break;
                case 29:
                    aDataSize = 116;
                    aDataCount = 7;
                    bDataSize = 117;
                    bDataCount = 7;
                    ecLength = 30;
                    break;
                case 30:
                    aDataSize = 115;
                    aDataCount = 5;
                    bDataSize = 116;
                    bDataCount = 10;
                    ecLength = 30;
                    break;
                case 31:
                    aDataSize = 115;
                    aDataCount = 13;
                    bDataSize = 116;
                    bDataCount = 3;
                    ecLength = 30;
                    break;
                case 32:
                    aDataSize = 0;
                    aDataCount = 0;
                    bDataSize = 115;
                    bDataCount = 17;
                    ecLength = 30;
                    break;
                case 33:
                    aDataSize = 115;
                    aDataCount = 17;
                    bDataSize = 116;
                    bDataCount = 1;
                    ecLength = 30;
                    break;
                case 34:
                    aDataSize = 115;
                    aDataCount = 13;
                    bDataSize = 116;
                    bDataCount = 6;
                    ecLength = 30;
                    break;
                case 35:
                    aDataSize = 121;
                    aDataCount = 12;
                    bDataSize = 122;
                    bDataCount = 7;
                    ecLength = 30;
                    break;
                case 36:
                    aDataSize = 121;
                    aDataCount = 6;
                    bDataSize = 122;
                    bDataCount = 14;
                    ecLength = 30;
                    break;
                case 37:
                    aDataSize = 122;
                    aDataCount = 17;
                    bDataSize = 123;
                    bDataCount = 4;
                    ecLength = 30;
                    break;
                case 38:
                    aDataSize = 122;
                    aDataCount = 4;
                    bDataSize = 123;
                    bDataCount = 18;
                    ecLength = 30;
                    break;
                case 39:
                    aDataSize = 117;
                    aDataCount = 20;
                    bDataSize = 118;
                    bDataCount = 4;
                    ecLength = 30;
                    break;
                case 40:
                    aDataSize = 118;
                    aDataCount = 19;
                    bDataSize = 119;
                    bDataCount = 6;
                    ecLength = 30;
                    break;
                default:
                    aDataSize = default;
                    aDataCount = default;
                    bDataSize = default;
                    bDataCount = default;
                    ecLength = default;
                    break;
            }
        }

#if !COMPILER_UDONSHARP
        public
#endif
        byte GetMatrixSize(byte version)
        {
            return (byte)(version * 4 + 17);
        }
    }
}
