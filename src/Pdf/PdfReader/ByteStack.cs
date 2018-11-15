using System;

namespace QaKit.FileComparer.PDF.PdfReader
{
	/// <summary>
	/// A stack based (FIFO) structure for the <see cref="System.Byte"/> type.
	/// </summary>
	class ByteStack
	{
		private byte[] _buffer;
		private int _size;
		private int _count;
		private readonly int _growFactor = 2;

		public ByteStack()
		{
			_size = 16;
			_count = 0;
			_buffer = new byte[_size];
		}


		public void Push(byte ch)
		{
			EnsureSize(_count + 1);
			_buffer[_count] = ch;
			_count++;
		}


		public byte Pop()
		{
			if (_count < 1)
				throw new IndexOutOfRangeException();

			return _buffer[--_count];
		}


		public byte Peek()
		{
			if (_count < 1)
				throw new IndexOutOfRangeException();

			return _buffer[_count - 1];
		}

		public int Count
		{
			get
			{
				return _count;
			}
		}

		private void EnsureSize(int size)
		{
			if (size > _size)
			{
				byte[] swap = new byte[_size * _growFactor];
				Array.Copy(_buffer, swap, _size);
				_buffer = swap;
				_size = _size * _growFactor;
			}
		}
	}
}
