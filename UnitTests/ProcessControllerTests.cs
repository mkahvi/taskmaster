﻿using System;
using Taskmaster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MKAh;

namespace TaskMasterTests
{
	[TestClass]
	public class ProcessControllerTests
	{
		public TestContext TestContext { get; set; }

		[TestMethod]
		public void AffinityBitCountTests()
		{
			int testSource = 192;
			int testTarget = 240;
			Console.WriteLine("Source: " + testSource);
			Console.WriteLine("Target: " + testTarget);

			Assert.AreEqual(2, Bit.Count(testSource));
			Assert.AreEqual(4, Bit.Count(testTarget));

			int excesscores = Bit.Count(testTarget) - Bit.Count(testSource);
			Assert.AreEqual(2, excesscores);
		}

		[TestMethod]
		public void AffinityBitManipTests()
		{
			int mask = 0b11110000; // 240
			int expected = 0b110000; // 48

			mask = Bit.Unset(mask, 7);
			mask = Bit.Unset(mask, 6);

			Assert.AreEqual(expected, mask);
		}

		[TestMethod]
		public void CPUMaskTests()
		{
			int fakecpucount = 8;
			int expectedoffset = 23; // 0 to 23, not 1 to 24
			int offset = Bit.Count(int.MaxValue) - fakecpucount;
			Assert.AreEqual(expectedoffset, offset);
		}

		[TestMethod]
		public void AffinityStrategyTests()
		{
			int testSource = 192;
			int testTarget = 240;

			switch (ProcessManager.CPUCount)
			{
				case 4:
					testSource = 0b0100;
					testTarget = 0b1100;
					break;
				default:
					return;
			}

			Console.WriteLine("Source: " + testSource);
			Console.WriteLine("Target: " + testTarget);

			int testProduct = ProcessManagerUtility.ApplyAffinityStrategy(
				testSource, testTarget, ProcessAffinityStrategy.Limit);

			Assert.AreEqual(Bit.Count(testSource), Bit.Count(testProduct));
		}

		[TestMethod]
		public void AffinityTests()
		{
			int target = 240;
			int source = 192;
			int testmask = target;

			int testcpucount = 8;

			int excesscores = Bit.Count(target) - Bit.Count(source);
			TestContext.WriteLine("Excess: " + excesscores);
			if (excesscores > 0)
			{
				TestContext.WriteLine("Mask Base: " + Convert.ToString(testmask, 2));
				for (int i = 0; i < testcpucount; i++)
				{
					if (Bit.IsSet(testmask, i))
					{
						testmask = Bit.Unset(testmask, i);
						TestContext.WriteLine("Mask Modified: " + Convert.ToString(testmask, 2));
						if (--excesscores <= 0) break;
					}
					else
						TestContext.WriteLine("Bit not set: " + i);
				}
				TestContext.WriteLine("Mask Final: " + Convert.ToString(testmask, 2));
			}

			Assert.AreEqual(source, testmask);
		}
	}
}
