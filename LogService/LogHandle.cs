using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace LogService
{
	public class LogHandle : IDisposable
	{
		BlockingCollection<LogContent> loggers = new BlockingCollection<LogContent>();
		string LogExt = @"C:\Users\Egg\source\repos\LogService\Logger\bin\Debug\Logger.exe";
		Thread _writeThread = null;
		private bool _disposing = false;
		private MemoryMappedFile _memoryMapping = null;
		private Semaphore _logCounter = null;
		private LogParameter _paramter;

		public LogHandle(string logExt = null)
		{
			if (string.IsNullOrEmpty(logExt) == false)
			{
				LogExt = logExt;
			}

			var pid = Process.GetCurrentProcess().Id;

			_paramter = new LogParameter();
			_paramter.ParentId = pid;

			StartProcess(_paramter);

			//create mmf write file
			_memoryMapping = MemoryMappedFile.OpenExisting(_paramter.ShareMemoryName);
			_logCounter = Semaphore.OpenExisting(_paramter.SemaphoreName);

			_writeThread = new Thread(LogWork);
			_writeThread.Start();
		}

		private void StartProcess(LogParameter paramter)
		{
			//start process
			List<string> processLog = new List<string>();
			var process = new System.Diagnostics.Process();
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardInput = false;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.FileName = LogExt;
			process.StartInfo.Arguments = paramter.ToString();
			process.OutputDataReceived += (sender, e) =>
			{
				if (!string.IsNullOrEmpty(e.Data))
				{
					processLog.Add(e.Data); // 只添加非空行
				}
			};

			process.Start();
			process.BeginOutputReadLine();

			//wait for process start
			SpinWait.SpinUntil(() => processLog.Any(x => x == "ready"), 3000);
		}

		public void Dispose()
		{
			_disposing = true;
			loggers.Add(new LogContent() { Block = "exit", Message = "exit" });
			_writeThread.Join();
			_memoryMapping.Dispose();
			_logCounter.Dispose();
		}

		public void AddDebug(string header, string message)
		{
			var content = new LogContent() { Block = header, Message = message };
			loggers.Add(content);
		}

		public void LogWork()
		{
			int writePosition = 0;
			int structSize = Marshal.SizeOf<LogStruct>();
			LogStruct logStruct = new LogStruct();

			using (var mmfView = _memoryMapping.CreateViewAccessor(0, _paramter.RingBufferSize))
			{
				while (true)
				{
					var ret = loggers.Take();
					if (_disposing) return;
					
					var blockByte = Encoding.Unicode.GetBytes(ret.Block);
					var messageByte = Encoding.Unicode.GetBytes(ret.Message);
					var timeByte = Encoding.Unicode.GetBytes(ret.Time.ToString(LogParameter.TimeFormat));

					logStruct.BlockLength = blockByte.Length;
					logStruct.MessageLength = messageByte.Length;
					logStruct.TimeLength = timeByte.Length;
					var logStructByte = LogUtil.StructToBytes(logStruct);

					WriteToMmf(mmfView, _paramter.RingBufferSize, writePosition, logStructByte);
					writePosition = AddWritePosition(writePosition, structSize, _paramter.RingBufferSize);

					WriteToMmf(mmfView, _paramter.RingBufferSize, writePosition, blockByte);
					writePosition = AddWritePosition(writePosition, blockByte.Length, _paramter.RingBufferSize);

					WriteToMmf(mmfView, _paramter.RingBufferSize, writePosition, timeByte);
					writePosition = AddWritePosition(writePosition, timeByte.Length, _paramter.RingBufferSize);

					WriteToMmf(mmfView, _paramter.RingBufferSize, writePosition, messageByte);
					writePosition = AddWritePosition(writePosition, messageByte.Length, _paramter.RingBufferSize);

					_logCounter.Release();
				}
			}
		}

		private int AddWritePosition(int writePosition, int structSize, int ringBufferSize)
		{
			writePosition += structSize;
			writePosition = writePosition % ringBufferSize;
			return writePosition;
		}

		private void WriteToMmf(MemoryMappedViewAccessor mmfView, int ringBufferSize, int writePosition, byte[] data)
		{
			int dataSize = data.Length;

			// 檢查寫入是否會跨越環形緩衝區的結尾
			if (writePosition + dataSize > ringBufferSize)
			{
				// 跨越環形緩衝區，需要將資料分成兩部分寫入
				int firstPartSize = (int)(ringBufferSize - writePosition);
				int secondPartSize = dataSize - firstPartSize;

				// 第一部分寫入到環形緩衝區的結尾
				mmfView.WriteArray(writePosition, data, 0, firstPartSize);

				// 第二部分寫入到環形緩衝區的起始位置
				mmfView.WriteArray(0, data, firstPartSize, secondPartSize);
			}
			else
			{
				// 不會跨越環形緩衝區，直接寫入
				mmfView.WriteArray(writePosition, data, 0, dataSize);
			}
		}
	}

	internal class LogContent
	{
		public string Block { get; set; }
		public string Message { get; set; }
		public DateTime Time { get; } = DateTime.Now;
	}

	public enum LogType
	{
		Info,
		Error,
		Warning,
		Debug
	}
}
