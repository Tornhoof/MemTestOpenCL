using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.OpenCL;

namespace MemTestOpenCL
{
	public class Program
	{
		static int Main(string[] args)
		{
			int iterations = 10;
			if (args.Length == 1)
			{
				iterations = int.Parse(args[0]);
			}
			for (int i = 0; i < iterations; i++)
			{
				if (MemTest.FindErrors())
				{
					Console.WriteLine("Found possible error.");
					return -1;
				}
			}

			return 0;
		}
	}
	/// <summary>
	/// Quick & dirty attempt to allocate much memory on the GPU and fill it with different kinds of data, then read it out again and check if it is the same
	/// </summary>
	internal static unsafe class MemTest
	{
		public static bool FindErrors()
		{
			using var cl = CL.GetApi();
			// Output a bit of general information
			nint platform;
			nint device;
			var err = cl.GetPlatformIDs(1, &platform, null);
			AssertZero(err);
			err = cl.GetDeviceIDs(platform, CLEnum.DeviceTypeGpu, 1, &device, null);
			AssertZero(err);
			Span<byte> buffer = stackalloc byte[256];
			fixed (byte* ptr = buffer)
			{
				nuint length;
				err = cl.GetPlatformInfo(platform, (uint)CLEnum.PlatformName, 256, ptr, &length);
				AssertZero(err);
				Console.WriteLine("Platform: {0}", Encoding.UTF8.GetString(buffer.Slice(0, (int)length)));

				err = cl.GetDeviceInfo(device, (uint)CLEnum.DeviceName, 256, ptr, &length);
				AssertZero(err);
				Console.WriteLine("Device: {0}", Encoding.UTF8.GetString(buffer.Slice(0, (int)length)));
			}

			nuint globalmemsize;
			err = cl.GetDeviceInfo(device, (uint)CLEnum.DeviceGlobalMemSize, (nuint)sizeof(nuint), &globalmemsize, null);
			AssertZero(err);
			Console.WriteLine("Memory: {0}", globalmemsize);

			var props = stackalloc nint[3];
			props[0] = (nint)CLEnum.ContextPlatform;
			props[0] = 0;
			props[0] = 0;
			int size = 1 << 16;
			// Try to allocate a max size buffer, we do this in 64Mb steps until we succeed.
			Span<nint> input = new nint[size];
			Span<nint> output = new nint[size];
			nuint stridesize = (nuint)(size * sizeof(nuint));
			fixed (nint* inputPtr = input)
			fixed (nint* outputPtr = output)
			{
				nint ctx = cl.CreateContext(props, 1, &device, null, null, &err);
				AssertZero(err);

				nint queue = cl.CreateCommandQueue(ctx, device, 0, &err);
				AssertZero(err);
				nuint memSize = globalmemsize;
				nint dA;
				do
				{
					dA = cl.CreateBuffer(ctx, CLEnum.MemReadWrite, memSize, null, &err);
					AssertZero(err);

					Console.WriteLine("Trying to allocate: {0} bytes", memSize);
					err = cl.EnqueueWriteBuffer
						(queue, dA, true, 0, stridesize, *inputPtr, 0, null, null);
					if (err != 0)
					{
						cl.ReleaseMemObject(dA);
						memSize -= 1 << 26; // use other value for smaller steps
					}
				} while (err != 0);

				AssertZero(err);
				err = cl.Finish(queue);
				AssertZero(err);

				Console.WriteLine("Successfully allocated: {0} bytes", memSize);

				nuint strides = memSize / stridesize;
				bool errorFound = false;
				var values = Enum.GetValues<Pattern>();
				foreach (var scenario in values) // different patterns
				{
					Console.Clear();
					errorFound |= TryFindError(cl, scenario, strides, queue, dA, stridesize, inputPtr, outputPtr, input, output, errorFound, memSize);
					if (errorFound)
					{
						break;
					}
				}

				cl.ReleaseMemObject(dA);
				cl.ReleaseCommandQueue(queue);
				cl.ReleaseContext(ctx);

				return errorFound;
			}
		}

		private static bool TryFindError(CL cl, Pattern pattern, nuint strides, nint queue, nint dA, nuint stridesize,
			nint* inputPtr, nint* outputPtr, Span<nint> input, Span<nint> output, bool errorFound, nuint memSize)
		{
			Span<byte> inputBytes = MemoryMarshal.AsBytes(input);
			Span<byte> outputBytes = MemoryMarshal.AsBytes(output);
			nuint offset = 0;
			for (int strideIndex = 0; strideIndex < (int)strides; strideIndex++)
			{
				Random.Shared.NextBytes(outputBytes); // make sure we fill the previous buffer with random data for the 0,1, etc patterns
				switch (pattern)
				{
					case Pattern.Random:
						Random.Shared.NextBytes(inputBytes);
						break;
					case Pattern.Zero:
						inputBytes.Fill(0);
						break;
					case Pattern.OneZeroPattern:
						inputBytes.Fill(0b01010101);
						break;
					case Pattern.ZeroOnePattern:
						inputBytes.Fill(0b10101010);
						break;
					case Pattern.One:
						inputBytes.Fill(0xFF);
						break;
				}

				var err = cl.EnqueueWriteBuffer(queue, dA, true, offset, stridesize, inputPtr, 0, null, null);
				AssertZero(err);
				err = cl.Finish(queue);
				AssertZero(err);
				err = cl.EnqueueReadBuffer(queue, dA, true, offset, stridesize, outputPtr, 0, null, null);
				AssertZero(err);
				err = cl.Finish(queue);
				AssertZero(err);


				if (!input.SequenceEqual(output))
				{
					Console.WriteLine("Bad memory at offset {0}.", offset);
					errorFound = true;
					break;
				}

				if (strideIndex % 1000 == 0)
				{
					Console.SetCursorPosition(0, 0);
					Console.WriteLine("Completed {0:F}% in scenario {1}", (offset / (double)memSize) * 100.0d, pattern);
				}

				offset += stridesize;
			}

			return errorFound;
		}

		private static void AssertZero(int i)
		{
			if (i != 0)
			{
				throw new InvalidOperationException($"Error code is not zero: {(CLEnum)i}");
			}
		}

		public enum Pattern
		{
			Random,
			Zero,
			OneZeroPattern,
			ZeroOnePattern,
			One,

		}
	}
}
