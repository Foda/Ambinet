using SharpDX.DXGI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Ambinet
{
    public class ScreenCapture
    {
        public Bitmap CapturedFrame { get; set; }

        static Bitmap GetImageFromDXStream(int Width, int Height, SharpDX.DataStream stream)
        {
            var b = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            var BoundsRect = new Rectangle(0, 0, Width, Height);
            BitmapData bmpData = b.LockBits(BoundsRect, ImageLockMode.WriteOnly, b.PixelFormat);
            int bytes = bmpData.Stride * b.Height;
            var rgbValues = new byte[bytes * 4];

            // copy bytes from the surface's data stream to the bitmap stream
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    stream.Seek(y * (Width * 4) + x * 4, System.IO.SeekOrigin.Begin);
                    stream.Read(rgbValues, y * (Width * 4) + x * 4, 4);
                }
            }

            Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
            b.UnlockBits(bmpData);
            return b;
        }

        public ScreenCapture()
        {
            CapturedFrame = new Bitmap(1920, 1080);
        }

        public void CaptureLoop()
        {
            uint numAdapter = 0;   // # of graphics card adapter
            uint numOutput = 0;    // # of output device (i.e. monitor)

            // create device and factory
            SharpDX.Direct3D11.Device device = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware);
            Factory1 factory = new Factory1();

            // creating CPU-accessible texture resource
            SharpDX.Direct3D11.Texture2DDescription texdes = new SharpDX.Direct3D11.Texture2DDescription();
            texdes.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read;
            texdes.BindFlags = SharpDX.Direct3D11.BindFlags.None;
            texdes.Format = Format.B8G8R8A8_UNorm;
            texdes.Height = factory.Adapters1[numAdapter].Outputs[numOutput].Description.DesktopBounds.Height;
            texdes.Width = factory.Adapters1[numAdapter].Outputs[numOutput].Description.DesktopBounds.Width;
            texdes.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;
            texdes.MipLevels = 1;
            texdes.ArraySize = 1;
            texdes.SampleDescription.Count = 1;
            texdes.SampleDescription.Quality = 0;
            texdes.Usage = SharpDX.Direct3D11.ResourceUsage.Staging;
            SharpDX.Direct3D11.Texture2D screenTexture = new SharpDX.Direct3D11.Texture2D(device, texdes);

            // duplicate output stuff
            Output1 output = new Output1(factory.Adapters1[0].Outputs[numOutput].NativePointer);
            OutputDuplication duplicatedOutput = output.DuplicateOutput(device);
            Resource screenResource = null;
            SharpDX.DataStream dataStream;
            Surface screenSurface;

            int i = 0;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (true)
            {
                i++;

                // try to get duplicated frame within given time
                try
                {
                    OutputDuplicateFrameInformation duplicateFrameInformation;
                    duplicatedOutput.AcquireNextFrame(1000, out duplicateFrameInformation, out screenResource);
                }
                catch (SharpDX.SharpDXException e)
                {
                    if (e.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                    {
                        // this has not been a successful capture
                        // thanks @Randy
                        i--;

                        // keep retrying
                        continue;
                    }
                    else
                    {
                        throw e;
                    }
                }

                // copy resource into memory that can be accessed by the CPU
                device.ImmediateContext.CopyResource(screenResource.QueryInterface<SharpDX.Direct3D11.Resource>(), screenTexture);

                // cast from texture to surface, so we can access its bytes
                screenSurface = screenTexture.QueryInterface<Surface>();

                // map the resource to access it
                screenSurface.Map(MapFlags.Read, out dataStream);

                lock (CapturedFrame)
                {
                    CapturedFrame = GetImageFromDXStream(10, 10, dataStream);
                }

                // seek within the stream and read one byte
                dataStream.Position = 4;
                dataStream.ReadByte();

                // free resources
                dataStream.Close();
                screenSurface.Unmap();
                screenSurface.Dispose();
                screenResource.Dispose();
                duplicatedOutput.ReleaseFrame();

                // print how many frames we could process within the last second
                // note that this also depends on how often windows will >need< to redraw the interface
                if (sw.ElapsedMilliseconds > 1000)
                {
                    Console.WriteLine(i + "fps");
                    sw.Reset();
                    sw.Start();
                    i = 0;
                }
            }
        }
    }
}
