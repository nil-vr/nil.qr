using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace Nil.Qr.Test
{
    public class QrCodeTests
    {
        string HexDump(byte[] data)
        {
            if (data.Length == 0)
            {
                return "";
            }
            var builder = new StringBuilder(data.Length * 3 - 1);
            var nibbles = "0123456789abcdef";
            
            builder.Append(nibbles[data[0] >> 4]);
            builder.Append(nibbles[data[0] & 0xf]);
            for (var i = 1; i < data.Length; i++)
            {
                builder.Append(' ');
                builder.Append(nibbles[data[i] >> 4]);
                builder.Append(nibbles[data[i] & 0xf]);
            }
            return builder.ToString();
        }

        Texture2D LoadPng(string name, [CallerFilePath] string testPath = null)
        {
            var path = Path.Combine(testPath, "..", $"{name}.png");
            var data = File.ReadAllBytes(path);
            var texture = new Texture2D(0, 0);
            try
            {
                if (!ImageConversion.LoadImage(texture, data))
                {
                    throw new Exception("Load failed");
                }
                return texture;
            }
            catch
            {
                Texture2D.DestroyImmediate(texture);
                throw;
            }
        }

        void SavePng(string name, byte[] data, uint width, [CallerFilePath] string testPath = null)
        {
            var path = Path.Combine(testPath, "..", $"{name}.png");
            var png = ImageConversion.EncodeArrayToPNG(data, GraphicsFormat.R8_UNorm, width, (uint)(data.Length / width), width);
            File.WriteAllBytes(path, png);
        }

        void SavePng(string name, Texture2D texture, [CallerFilePath] string testPath = null)
        {
            var path = Path.Combine(testPath, "..", $"{name}.png");
            var png = ImageConversion.EncodeToPNG(texture);
            File.WriteAllBytes(path, png);
        }

        void AssertPng(string name, byte[] data)
        {
            var expected = LoadPng(name);
            try
            {
                var expectedData = new byte[expected.width * expected.height];
                for (var y = 0; y < expected.height; y++)
                {
                    for (var x = 0; x < expected.width; x++)
                    {
                        expectedData[y * expected.width + x] = (byte)(expected.GetPixel(x, y).grayscale * 255);
                    }
                }
                try
                {
                    Assert.AreEqual(expectedData, data);
                }
                catch
                {
                    SavePng($"{name}-failed", data, (uint)expected.width);
                    Debug.Log($"Saved failure to {name}-failed");
                    throw;
                }
            }
            finally
            {
                Texture2D.DestroyImmediate(expected);
            }
        }

        void AssertPng(string name, Texture2D actual)
        {
            try
            {
                var expected = LoadPng(name);
                try
                {
                    var expectedData = new byte[expected.width * expected.height];
                    for (var y = 0; y < expected.height; y++)
                    {
                        for (var x = 0; x < expected.width; x++)
                        {
                            expectedData[y * expected.width + x] = (byte)(expected.GetPixel(x, y).r * 255);
                        }
                    }
                    var actualData = new byte[actual.width * actual.height];
                    for (var y = 0; y < actual.height; y++)
                    {
                        for (var x = 0; x < actual.width; x++)
                        {
                            actualData[y * actual.width + x] = (byte)(actual.GetPixel(x, y).r * 255);
                        }
                    }
                    try
                    {
                        Assert.AreEqual(expectedData, actualData);
                    }
                    catch
                    {
                        SavePng($"{name}-failed", actual);
                        Debug.Log($"Saved failure to {name}-failed");
                        throw;
                    }
                }
                finally
                {
                    Texture2D.DestroyImmediate(expected);
                }
            }
            finally
            {
                Texture2D.DestroyImmediate(actual);
            }
        }

        QrCode GetBehavior()
        {
            var go = new GameObject();
            // QrCode will try to add this automatically, but we don't wait for it to do so.
            go.AddComponent<RawImage>();
            return go.AddComponent<QrCode>();
        }

        [Test]
        public void RoundUpToQrLength_ReturnsCorrectLength()
        {
            var qrCode = GetBehavior();
            var edges = new[] {17,32,53,78,106,134,154,192,230,271,321,367,425,458,520,586,644,718,792,858,929,1003,1091,1171,1273,1367,1465,1528,1628,1732,1840,1952,2068,2188,2303,2431,2563,2699,2809,2953};
            for (ushort i = 0, j = 0; i <= 2953; i++)
            {
                if (i > edges[j])
                {
                    j++;
                }
                Assert.AreEqual(edges[j], qrCode.RoundUpToQrLength(i, out var version), $"{i}");
                Assert.AreEqual(j + 1, version);
            }
        }

        [Test]
        public void Encode_EncodesCorrectly()
        {
            var qrCode = GetBehavior();
            var text = "hello";
            qrCode.MeasureData(text, out var version, out var contentLength, out var dataLength);
            Assert.AreEqual(version, 1);
            var buffer = new byte[dataLength + 2];
            qrCode.Encode(text, version, buffer, 1, contentLength, dataLength);
            var expected = new byte[] { 0x00, 0x40, 0x56, 0x86, 0x56, 0xc6, 0xc6, 0xf0, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0x00 };
            Assert.AreEqual(HexDump(expected), HexDump(buffer));
        }

        [Test]
        public void Encoded_WithUnicode_EncodesCorrectly()
        {
            var qrCode = GetBehavior();
            // When encoded as utf-8, these characters take one byte, two bytes, three bytes, four bytes.
            var text = "!¡‼🗣";
            qrCode.MeasureData(text, out var version, out var contentLength, out var dataLength);
            Assert.AreEqual(version, 1);
            var buffer = new byte[dataLength];
            qrCode.Encode(text, version, buffer, 0, contentLength, dataLength);

            var expected = Encoding.UTF8.GetBytes(text);
            var actual = new byte[expected.Length];
            for (var i = 0; i < actual.Length; i++)
            {
                actual[i] = (byte)((buffer[i + 1] << 4) | buffer[i + 2] >> 4);
            }
            Assert.AreEqual(HexDump(expected), HexDump(actual));
        }

        [Test]
        public void GenerateErrorCorrectionBlock_Works()
        {
            var qrCode = GetBehavior();
            var buffer = new byte[] {0x00, 0x80, 0x56, 0x86, 0x56, 0xc6, 0xc6, 0xf0, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
            qrCode.GenerateErrorCorrectionBlock(buffer, 1, 19, qrCode.GetPolynomial(7), qrCode.GetLogs(), qrCode.GetAntilogs());
            var expected = new byte[] {0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x90, 0x09, 0xb6, 0xf7, 0x18, 0x36, 0x00, 0x00};
            Assert.AreEqual(HexDump(expected), HexDump(buffer));
        }

        [Test]
        public void GenerateDataLayout_ForVersion1_Works()
        {
            var qrCode = GetBehavior();
            var input = new byte[] { 0x80, 0x56, 0x86, 0x56, 0xc6, 0xc6, 0xf0, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11 };
            qrCode.GetErrorBlockSizes(1, out var aSize, out var aCount, out var bSize, out var bCount, out var ecLength);
            var buffer = new byte[input.Length + input.Length + (aCount + bCount) * ecLength + Math.Max(aSize, bSize) + ecLength];
            Array.Copy(input, 0, buffer, 0, input.Length);

            qrCode.GenerateDataLayout(buffer, 0, aSize, aCount, bSize, bCount, ecLength, input.Length + input.Length + (aCount + bCount) * ecLength, input.Length);

            var layout = new byte[input.Length + (aCount + bCount) * ecLength];
            Array.Copy(buffer, input.Length, layout, 0, layout.Length);
            var expected = new byte[] {0x80, 0x56, 0x86, 0x56, 0xc6, 0xc6, 0xf0, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0x90, 0x09, 0xb6, 0xf7, 0x18, 0x36, 0x00};
            Assert.AreEqual(HexDump(expected), HexDump(layout));
        }

        [Test]
        public void GenerateDataLayout_ForVersion2_Works()
        {
            var qrCode = GetBehavior();
            var input = new byte[] { 0x42, 0x00, 0x00, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0, 0xf1, 0x01, 0x11, 0x21, 0x31, 0x41, 0x51, 0x61, 0x71, 0x81, 0x91, 0xa1, 0xb1, 0xc1, 0xd1, 0xe1, 0xf0 };
            qrCode.GetErrorBlockSizes(2, out var aSize, out var aCount, out var bSize, out var bCount, out var ecLength);
            var buffer = new byte[input.Length + input.Length + (aCount + bCount) * ecLength + Math.Max(aSize, bSize) + ecLength];
            Array.Copy(input, 0, buffer, 0, input.Length);

            qrCode.GenerateDataLayout(buffer, 0, aSize, aCount, bSize, bCount, ecLength, input.Length + input.Length + (aCount + bCount) * ecLength, input.Length);

            var layout = new byte[input.Length + (aCount + bCount) * ecLength];
            Array.Copy(buffer, input.Length, layout, 0, layout.Length);
            var expected = new byte[] { 0x42, 0x00, 0x00, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0, 0xf1, 0x01, 0x11, 0x21, 0x31, 0x41, 0x51, 0x61, 0x71, 0x81, 0x91, 0xa1, 0xb1, 0xc1, 0xd1, 0xe1, 0xf0, 0xbb, 0x3b, 0xba, 0x86, 0x1a, 0xb1, 0x98, 0xc1, 0x0a, 0x06 };
            Assert.AreEqual(HexDump(expected), HexDump(layout));
        }

        [Test]
        public void GenerateDataLayout_ForVersion10_Works()
        {
            var qrCode = GetBehavior();
            var input = new byte[] { 0x40, 0x10, 0xf0, 0x00, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0, 0xf1, 0x01, 0x11, 0x21, 0x31, 0x41, 0x51, 0x61, 0x71, 0x81, 0x91, 0xa1, 0xb1, 0xc1, 0xd1, 0xe1, 0xf2, 0x02, 0x12, 0x22, 0x32, 0x42, 0x52, 0x62, 0x72, 0x82, 0x92, 0xa2, 0xb2, 0xc2, 0xd2, 0xe2, 0xf3, 0x03, 0x13, 0x23, 0x33, 0x43, 0x53, 0x63, 0x73, 0x83, 0x93, 0xa3, 0xb3, 0xc3, 0xd3, 0xe3, 0xf4, 0x04, 0x14, 0x24, 0x34, 0x44, 0x54, 0x64, 0x74, 0x84, 0x94, 0xa4, 0xb4, 0xc4, 0xd4, 0xe4, 0xf5, 0x05, 0x15, 0x25, 0x35, 0x45, 0x55, 0x65, 0x75, 0x85, 0x95, 0xa5, 0xb5, 0xc5, 0xd5, 0xe5, 0xf6, 0x06, 0x16, 0x26, 0x36, 0x46, 0x56, 0x66, 0x76, 0x86, 0x96, 0xa6, 0xb6, 0xc6, 0xd6, 0xe6, 0xf7, 0x07, 0x17, 0x27, 0x37, 0x47, 0x57, 0x67, 0x77, 0x87, 0x97, 0xa7, 0xb7, 0xc7, 0xd7, 0xe7, 0xf0, 0x00, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0, 0xf1, 0x01, 0x11, 0x21, 0x31, 0x41, 0x51, 0x61, 0x71, 0x81, 0x91, 0xa1, 0xb1, 0xc1, 0xd1, 0xe1, 0xf2, 0x02, 0x12, 0x22, 0x32, 0x42, 0x52, 0x62, 0x72, 0x82, 0x92, 0xa2, 0xb2, 0xc2, 0xd2, 0xe2, 0xf3, 0x03, 0x13, 0x23, 0x33, 0x43, 0x53, 0x63, 0x73, 0x83, 0x93, 0xa3, 0xb3, 0xc3, 0xd3, 0xe3, 0xf4, 0x04, 0x14, 0x24, 0x34, 0x44, 0x54, 0x64, 0x74, 0x84, 0x94, 0xa4, 0xb4, 0xc4, 0xd4, 0xe4, 0xf5, 0x05, 0x15, 0x25, 0x35, 0x45, 0x55, 0x65, 0x75, 0x85, 0x95, 0xa5, 0xb5, 0xc5, 0xd5, 0xe5, 0xf6, 0x06, 0x16, 0x26, 0x36, 0x46, 0x56, 0x66, 0x76, 0x86, 0x96, 0xa6, 0xb6, 0xc6, 0xd6, 0xe6, 0xf7, 0x07, 0x17, 0x27, 0x37, 0x47, 0x57, 0x67, 0x77, 0x87, 0x97, 0xa7, 0xb7, 0xc7, 0xd7, 0xe7, 0xf0, 0x00, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0 };
            qrCode.GetErrorBlockSizes(10, out var aSize, out var aCount, out var bSize, out var bCount, out var ecLength);
            var buffer = new byte[input.Length + input.Length + (aCount + bCount) * ecLength + Math.Max(aSize, bSize) + ecLength];
            Array.Copy(input, 0, buffer, 0, input.Length);

            qrCode.GenerateDataLayout(buffer, 0, aSize, aCount, bSize, bCount, ecLength, input.Length + input.Length + (aCount + bCount) * ecLength, input.Length);

            var layout = new byte[input.Length + (aCount + bCount) * ecLength];
            Array.Copy(buffer, input.Length, layout, 0, layout.Length);
            var expected = new byte[] { 0x40, 0x14, 0x50, 0xa4, 0x10, 0x24, 0x60, 0xb4, 0xf0, 0x34, 0x70, 0xc4, 0x00, 0x44, 0x80, 0xd4, 0x10, 0x54, 0x90, 0xe4, 0x20, 0x64, 0xa0, 0xf5, 0x30, 0x74, 0xb0, 0x05, 0x40, 0x84, 0xc0, 0x15, 0x50, 0x94, 0xd0, 0x25, 0x60, 0xa4, 0xe0, 0x35, 0x70, 0xb4, 0xf1, 0x45, 0x80, 0xc4, 0x01, 0x55, 0x90, 0xd4, 0x11, 0x65, 0xa0, 0xe4, 0x21, 0x75, 0xb0, 0xf5, 0x31, 0x85, 0xc0, 0x05, 0x41, 0x95, 0xd0, 0x15, 0x51, 0xa5, 0xe0, 0x25, 0x61, 0xb5, 0xf1, 0x35, 0x71, 0xc5, 0x01, 0x45, 0x81, 0xd5, 0x11, 0x55, 0x91, 0xe5, 0x21, 0x65, 0xa1, 0xf6, 0x31, 0x75, 0xb1, 0x06, 0x41, 0x85, 0xc1, 0x16, 0x51, 0x95, 0xd1, 0x26, 0x61, 0xa5, 0xe1, 0x36, 0x71, 0xb5, 0xf2, 0x46, 0x81, 0xc5, 0x02, 0x56, 0x91, 0xd5, 0x12, 0x66, 0xa1, 0xe5, 0x22, 0x76, 0xb1, 0xf6, 0x32, 0x86, 0xc1, 0x06, 0x42, 0x96, 0xd1, 0x16, 0x52, 0xa6, 0xe1, 0x26, 0x62, 0xb6, 0xf2, 0x36, 0x72, 0xc6, 0x02, 0x46, 0x82, 0xd6, 0x12, 0x56, 0x92, 0xe6, 0x22, 0x66, 0xa2, 0xf7, 0x32, 0x76, 0xb2, 0x07, 0x42, 0x86, 0xc2, 0x17, 0x52, 0x96, 0xd2, 0x27, 0x62, 0xa6, 0xe2, 0x37, 0x72, 0xb6, 0xf3, 0x47, 0x82, 0xc6, 0x03, 0x57, 0x92, 0xd6, 0x13, 0x67, 0xa2, 0xe6, 0x23, 0x77, 0xb2, 0xf7, 0x33, 0x87, 0xc2, 0x07, 0x43, 0x97, 0xd2, 0x17, 0x53, 0xa7, 0xe2, 0x27, 0x63, 0xb7, 0xf3, 0x37, 0x73, 0xc7, 0x03, 0x47, 0x83, 0xd7, 0x13, 0x57, 0x93, 0xe7, 0x23, 0x67, 0xa3, 0xf0, 0x33, 0x77, 0xb3, 0x00, 0x43, 0x87, 0xc3, 0x10, 0x53, 0x97, 0xd3, 0x20, 0x63, 0xa7, 0xe3, 0x30, 0x73, 0xb7, 0xf4, 0x40, 0x83, 0xc7, 0x04, 0x50, 0x93, 0xd7, 0x14, 0x60, 0xa3, 0xe7, 0x24, 0x70, 0xb3, 0xf0, 0x34, 0x80, 0xc3, 0x00, 0x44, 0x90, 0xd3, 0x10, 0x54, 0xa0, 0xe3, 0x20, 0x64, 0xb0, 0xf4, 0x30, 0x74, 0xc0, 0x04, 0x40, 0x84, 0xd0, 0x94, 0xe0, 0x03, 0x2c, 0xec, 0x7c, 0xf2, 0x77, 0xed, 0xc1, 0x0d, 0xe2, 0x4c, 0x7e, 0x3f, 0xfb, 0x00, 0x77, 0xba, 0x47, 0xbd, 0xee, 0xce, 0x6f, 0xe3, 0xfe, 0xa4, 0xb0, 0xf9, 0x0a, 0x67, 0xa8, 0x49, 0xdd, 0xfb, 0x31, 0x41, 0x80, 0x96, 0xf6, 0x74, 0x80, 0xc2, 0xc9, 0xdf, 0xe5, 0xe6, 0xeb, 0xd2, 0x8e, 0xa1, 0xa5, 0xfa, 0xc4, 0xf0, 0xbe, 0x64, 0x4e, 0xba, 0xcb, 0x8f, 0x9e, 0xbf, 0x42, 0xa3, 0x21, 0x03, 0x1e, 0xc0, 0x11, 0xca, 0x5b, 0xbd, 0xd8 };
            Assert.AreEqual(HexDump(expected), HexDump(layout));
        }

        [Test]
        public void CreateEmptyImage_ForVersion2_CreatesCorrectImage()
        {
            var qrCode = GetBehavior();
            var matrixSize = qrCode.GetMatrixSize(2);
            var image = new byte[(2 * QrCode.MARGIN + matrixSize) * (2 * QrCode.MARGIN + matrixSize)];
            qrCode.CreateEmptyImage(image, 0, 2, matrixSize);
            AssertPng("empty-v2", image);
        }

        [Test]
        public void CreateEmptyImage_ForVersion7_CreatesCorrectImage()
        {
            var qrCode = GetBehavior();
            var matrixSize = qrCode.GetMatrixSize(7);
            var image = new byte[(2 * QrCode.MARGIN + matrixSize) * (2 * QrCode.MARGIN + matrixSize)];
            qrCode.CreateEmptyImage(image, 0, 7, matrixSize);
            AssertPng("empty-v7", image);
        }

        [Test]
        public void ApplyMask_ForMask0_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 0, 4 * 29 + 4, 21, 29);
            AssertPng("mask0", image);
        }

        [Test]
        public void ApplyMask_ForMask1_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 1, 4 * 29 + 4, 21, 29);
            AssertPng("mask1", image);
        }

        [Test]
        public void ApplyMask_ForMask2_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 2, 4 * 29 + 4, 21, 29);
            AssertPng("mask2", image);
        }

        [Test]
        public void ApplyMask_ForMask3_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 3, 4 * 29 + 4, 21, 29);
            AssertPng("mask3", image);
        }

        [Test]
        public void ApplyMask_ForMask4_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 4, 4 * 29 + 4, 21, 29);
            AssertPng("mask4", image);
        }

        [Test]
        public void ApplyMask_ForMask5_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 5, 4 * 29 + 4, 21, 29);
            AssertPng("mask5", image);
        }

        [Test]
        public void ApplyMask_ForMask6_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 6, 4 * 29 + 4, 21, 29);
            AssertPng("mask6", image);
        }

        [Test]
        public void ApplyMask_ForMask7_MasksCorrectBits()
        {
            var qrCode = GetBehavior();
            var image = new byte[29 * 29];
            qrCode.ApplyMask(image, 7, 4 * 29 + 4, 21, 29);
            AssertPng("mask7", image);
        }

        [Test]
        public void Render_ForVersion1_RendersCorrectly()
        {
            var qrCode = GetBehavior();
            AssertPng("hello", qrCode.Render("hello"));
        }

        [Test]
        public void Render_ForVersion7_RendersCorrectly()
        {
            var qrCode = GetBehavior();
            var input = "⓪①②③④⑤⑥⑦⑧⑨⓪①②③④⑤⑥⑦⑧⑨⓪①②③④⑤⑥⑦⑧⑨⓪①②③④⑤⑥⑦⑧⑨⓪①②③④⑤⑥⑦⑧⑨";
            qrCode.MeasureData(input, out var version, out var contentLength, out var dataLength);
            Assert.AreEqual(7, version);

            AssertPng("v7", qrCode.Render(input));
        }
    }
}
