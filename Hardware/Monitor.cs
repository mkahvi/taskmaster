﻿//
// Hardware.Monitor.cs
//
// Author:
//       M.A. (https://github.com/mkahvi)
//
// Copyright (c) 2018–2020 M.A.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using Serilog;
using System;
using System.Threading.Tasks;

namespace Taskmaster.Hardware
{
	public interface ILoadSensor
	{
		public float Load { get; set; }
	}

	public interface IFanSensor
	{
		public float FanLoad { get; set; }
		public float FanSpeed { get; set; }
	}

	public struct GPUSensors : ILoadSensor,IFanSensor
	{
		public float Load { get; set; }
		public float Clock { get; set; }

		public float MemLoad { get; set; }
		public float MemTotal { get; set; }

		public float MemCtrl { get; set; }

		public float FanLoad { get; set; }
		public float FanSpeed { get; set; }

		public float Temperature { get; set; }
	}

	public class GPUSensorEventArgs : EventArgs
	{
		public GPUSensors Data { get; set; }

		public GPUSensorEventArgs(GPUSensors data) => Data = data;
	}

	public struct CPUSensors : ILoadSensor,IFanSensor
	{
		public float Load { get; set; }

		public float FanLoad { get; set; }
		public float FanSpeed { get; set; }
	}

	public class CPUSensorEventArgs : EventArgs
	{
		public float Load { get; set; }

		public CPUSensorEventArgs(float load) => Load = load;
	}

	[Context(RequireMainThread = false)]
	public class Monitor : IComponent
	{
		OpenHardwareMonitor.Hardware.IHardware? gpu;
		OpenHardwareMonitor.Hardware.ISensor? gpuFan, // Fan speed
			gpuFanControl, // Fan speed controller. Value is % usage
			gpuTmp, // Temperature
			gpuMemLoad, // Free Memory
			gpuClock, // Core clock speed
			gpuLoad, // Core % load
			gpuMemCtrl, // Memory Controller
			//cpu,
			cpuLoad;
			//cpuFan,
			//cpuTmp,

		float gpuTotalMemory;

		readonly OpenHardwareMonitor.Hardware.Computer computer;

		public Monitor()
		{
			Log.Verbose("<Hardware> Initializing...");

			computer = new OpenHardwareMonitor.Hardware.Computer()
			{
				GPUEnabled = true,
				CPUEnabled = true,
				//MainboardEnabled = true, // doesn't seem to have anything
				FanControllerEnabled = true,
			};
			computer.Open();

			if (computer.Hardware.Length == 0)
			{
				computer.Close();
				throw new InitFailure("OHM failed to initialize.");
			}

			Application.OnStart += OnStart;

			Log.Verbose("<Hardware> Component loaded.");
		}

		bool Initialized;

		async void OnStart(object sender, EventArgs ea)
		{
			await Task.Delay(0).ConfigureAwait(false);

			try
			{
				foreach (var hw in computer.Hardware)
				{
					hw.Update();
					/*
					foreach (var shw in hw.SubHardware)
						shw.Update();
					*/

					/*
					 *
					 * CPU Core #1 : Load = 60.00
					 * CPU Core #2 : Load = 40.00
					 * CPU Total : Load = 50.00
					 * CPU Core #1 : Clock = 3,411.70
					 * CPU Core #2 : Clock = 3,411.70
					 *
					 * GPU Core : Temperature = 42.00
					 * GPU : Fan = 1,110.00
					 * GPU Core : Clock = 796.94
					 * GPU Memory : Clock = 3,004.68
					 * GPU Shader : Clock = 1,593.87
					 * GPU Core : Load = 3.00
					 * GPU Memory Controller : Load = 2.00
					 * GPU Video Engine : Load = 0.00 // this is "always" zero, useless stat
					 * GPU Fan : Control = 32.00
					 * GPU Memory Total : SmallData = 2,048.00
					 * GPU Memory Used : SmallData = 1,301.16
					 * GPU Memory Free : SmallData = 746.84
					 * GPU Memory : Load = 63.53
					 */

					Log.Verbose("Hardware: " + hw.Name + $" ({hw.HardwareType})");

					switch (hw.HardwareType)
					{
						case OpenHardwareMonitor.Hardware.HardwareType.GpuAti:
						case OpenHardwareMonitor.Hardware.HardwareType.GpuNvidia:
							GPUName = hw.Name;
							gpu = hw;
							foreach (var sensor in hw.Sensors)
							{
								Output(sensor);
								switch (sensor.Name)
								{
									default: break; // ignore
									case "GPU":
										if (sensor.SensorType == OpenHardwareMonitor.Hardware.SensorType.Fan)
											gpuFan = sensor;
										break;
									case "GPU Core":
										switch (sensor.SensorType)
										{
											case OpenHardwareMonitor.Hardware.SensorType.Temperature:
												gpuTmp = sensor;
												break;
											case OpenHardwareMonitor.Hardware.SensorType.Load:
												gpuLoad = sensor;
												break;
											case OpenHardwareMonitor.Hardware.SensorType.Clock:
												gpuClock = sensor;
												break;
										}
										break;
									case "GPU Fan":
										if (sensor.SensorType == OpenHardwareMonitor.Hardware.SensorType.Control)
											gpuFanControl = sensor;
										break;
									case "GPU Memory":
										if (sensor.SensorType == OpenHardwareMonitor.Hardware.SensorType.Load)
											gpuMemLoad = sensor;
										break;
									case "GPU Memory Total":
										if (sensor.SensorType == OpenHardwareMonitor.Hardware.SensorType.SmallData)
											gpuTotalMemory = sensor.Value ?? float.NaN;
										break;
									case "GPU Memory Controller":
										if (sensor.SensorType == OpenHardwareMonitor.Hardware.SensorType.Load)
											gpuMemCtrl = sensor;
										break;
								}
							}
							break;
						case OpenHardwareMonitor.Hardware.HardwareType.CPU:
							//cpu = hw;
							foreach (var sensor in hw.Sensors)
							{
								Output(sensor);
								switch (sensor.Name)
								{
									case "CPU Total":
										cpuLoad = sensor;
										break;
								}
							}
							break;
					}
				}

				Initialized = true;
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
			finally
			{
				computer?.Close(); // not needed?
			}
		}

		public float CPULoad
		{
			get
			{
				if (disposed) throw new ObjectDisposedException(nameof(Monitor), "CPULoad accessed after HardwareMonitor was disposed.");
				return cpuLoad.Value ?? float.NaN;
			}
		}

		public GPUSensors GPUSensorData()
		{
			if (disposed) throw new ObjectDisposedException(nameof(Monitor), "GPUSensorData accessed after HardwareMonitor was disposed.");
			if (!Initialized) throw new InvalidOperationException("GPUSensorData accesssed before HardwareMonitor was initialized");

			try
			{
				gpu.Update();
				return new GPUSensors
				{
					Clock = gpuClock.Value ?? float.NaN,
					FanLoad = gpuFanControl.Value ?? float.NaN,
					FanSpeed = gpuFan.Value ?? float.NaN,
					Load = gpuLoad.Value ?? float.NaN,
					MemLoad = gpuMemLoad.Value ?? float.NaN,
					MemTotal = gpuTotalMemory,
					MemCtrl = gpuMemCtrl.Value ?? float.NaN,
					Temperature = gpuTmp.Value ?? float.NaN,
				};
			}
			catch (Exception ex)
			{
				Logging.Stacktrace(ex);
				throw;
			}
		}

		public event EventHandler<GPUSensorEventArgs>? GPUPolling;
		public event EventHandler<CPUSensorEventArgs>? CPUPolling;

		System.Timers.Timer? SensorPoller;

		public void Start()
		{
			if (SensorPoller != null) return;
			if (disposed) throw new ObjectDisposedException(nameof(Monitor), "Start accessed after HardwareMonitor was disposed.");

			SensorPoller = new System.Timers.Timer(5000);
			SensorPoller.Elapsed += EmitGPU;
			SensorPoller.Elapsed += EmitCPU;
			SensorPoller.Start();
		}

		public void Stop()
		{
			SensorPoller?.Dispose();
			SensorPoller = null;
		}

		void EmitGPU(object _sender, System.Timers.ElapsedEventArgs _)
		{
			if (disposed) return;
			GPUPolling?.Invoke(this, new GPUSensorEventArgs(GPUSensorData()));
		}

		void EmitCPU(object _sender, System.Timers.ElapsedEventArgs _)
		{
			if (disposed) return;
			CPUPolling?.Invoke(this, new CPUSensorEventArgs(CPULoad));
		}

		public string GPUName { get; private set; } = string.Empty;

		void Output(OpenHardwareMonitor.Hardware.ISensor sensor)
		{
			if (disposed) return;

			float? tmp = sensor.Value;
			Log.Verbose(sensor.Name + " : " + sensor.SensorType.ToString() + " = " + (tmp.HasValue ? $"{tmp.Value:0.##}" : HumanReadable.Generic.NotAvailable));
		}

		#region IDisposable Support
		public event EventHandler<DisposedEventArgs>? OnDisposed;

		bool disposed; // To detect redundant calls

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) return;
			disposed = true;

			if (disposing)
			{
				Stop();
				GPUPolling = null;
				CPUPolling = null;

				computer?.Close();

				//base.Dispose();

				OnDisposed?.Invoke(this, DisposedEventArgs.Empty);
				OnDisposed = null;
			}
		}

		~Monitor() => Dispose(false);

		public void ShutdownEvent(object sender, EventArgs ea) => Stop();
		#endregion
	}
}
