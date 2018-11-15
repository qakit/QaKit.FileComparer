using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;

namespace QaKit.FileComparer.PDF.PdfReader
{
	class PdfScanner
	{
		private Stream _inputStream;
		private BinaryReader _reader;
		private ByteStack _pushBackChars;
		private static readonly Lexicals[] LexTable;// a table of characters with lexical information
		private static readonly Hashtable HexValueTable;

		/// <summary>
		/// Builds the lexTable
		/// </summary>
		static PdfScanner()
		{
			lock (typeof(PdfScanner))
			{
				LexTable = CreateLexTable();

				HexValueTable = new Hashtable();
				HexValueTable['0'] = 0;
				HexValueTable['1'] = 1;
				HexValueTable['2'] = 2;
				HexValueTable['3'] = 3;
				HexValueTable['4'] = 4;
				HexValueTable['5'] = 5;
				HexValueTable['6'] = 6;
				HexValueTable['7'] = 7;
				HexValueTable['8'] = 8;
				HexValueTable['9'] = 9;
				HexValueTable['A'] = 10;
				HexValueTable['a'] = 10;
				HexValueTable['B'] = 11;
				HexValueTable['b'] = 11;
				HexValueTable['C'] = 12;
				HexValueTable['c'] = 12;
				HexValueTable['D'] = 13;
				HexValueTable['d'] = 13;
				HexValueTable['E'] = 14;
				HexValueTable['e'] = 14;
				HexValueTable['F'] = 15;
				HexValueTable['f'] = 15;
			}
		}


		public PdfScanner(Stream inputStream)
		{
			if (inputStream == null)
				throw new ArgumentNullException("inputStream");

			_pushBackChars = new ByteStack();
			_inputStream = inputStream;
			_reader = new BinaryReader(inputStream, System.Text.Encoding.ASCII);
		}


		/// <summary>
		/// Reads the next character.
		/// </summary>
		/// <remarks>Any characters that have been pushed with <see cref="PushBack"/> will be returned before reading the next character from the input stream.</remarks>
		private byte Read()
		{
			byte b;

			if (_pushBackChars.Count > 0)
				b = _pushBackChars.Pop();
			else
				b = _reader.ReadByte();

			return b;
		}

		/// <summary>
		/// Just like <see cref="Read"/> performs the cast to char.
		/// </summary>
		/// <returns></returns>
		private char ReadAsChar()
		{
			return (char)Read();
		}

		/// <summary>
		/// Pushes the specified character onto the pushback stack so that the next call to <see cref="Read"/> will return it.
		/// </summary>
		private void PushBack(byte b)
		{
			_pushBackChars.Push(b);
		}

		private void PushBack(char ch)
		{
			_pushBackChars.Push((byte)ch);
		}

		/// <summary>
		/// Determines if the next character is the same as the specified character.
		/// If it is the same, it is read and the next call to Read will not return it.
		/// If it is not the same, it is not read, and the next call to Read will return this character.
		/// </summary>
		/// <param name="lookFor">The character to look ahead for.</param>
		/// <returns>True if the next character is the specified character otherwise false.</returns>
		private bool LookAhead(char lookFor)
		{
			char ch = ReadAsChar();
			if (ch == lookFor)
				return true;

			PushBack(ch);
			return false;
		}

		/// <summary>
		/// Determines if the specified sequence of bytes is directly ahead. If is, the bytes are read. 
		/// If the specified seequence is not ahead, this function restores the state so that 
		/// Read() will return the same character it would have before this function was called.
		/// </summary>
		/// <param name="forBytes"></param>
		/// <returns></returns>
		private bool LookAhead(byte[] forBytes)//internal for testing
		{
			for (int i = 0; i < forBytes.Length; i++)
			{
				byte bLookFor = forBytes[i];
				byte bRead = Read();

				if (bLookFor != bRead)
				{
					PushBack(bRead);
					// the forBytes is not head so put everything back the way it was before we got called and exit:
					for (int iRestore = i - 1; iRestore > -1; iRestore--)
					{
						PushBack(forBytes[iRestore]);
					}
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Scans the input stream to build the next token.
		/// </summary>
		/// <returns>False if the end of the stream is reached and no token can be returned.</returns>
		public bool NextToken(out Token token)
		{
			char ch = char.MinValue;
			try
			{
				ch = ReadAsChar();
			}
			catch (EndOfStreamException)
			{
				token = Token.Empty;
				return false;

			}

			if (ch == COMMENT)
				return ReadComment(out token);

			if (ch == SPACE)
			{
				EatWhiteSpace();
				return NextToken(out token);
			}
			if ((LexTable[ch] & Lexicals.IsEOF) == Lexicals.IsEOF)
				return NextToken(out token);

			// Name:
			if (ch == FORWARDSLASH)
				return ReadName(out token);

			if (((LexTable[ch] & Lexicals.IsNumber) == Lexicals.IsNumber)
				|| (ch == '+' || ch == '-' || ch == '.')
				)
			{
				PushBack(ch);
				return ReadNumber(out token);
			}

			if (ch == '<')
			{
				if (LookAhead('<'))
				{
					token = new Token(TokenType.DictionaryBegin, "<<");
					return true;
				}
				// else it's a hex string which will be handled below
			}
			if (ch == '>')
			{
				if (LookAhead('>'))
				{
					token = new Token(TokenType.DictionaryEnd, ">>");
					return true;
				}
				else
					throw new UnexpectedTokenException(">", this._inputStream.Position);
			}

			// PDF String (Literal) or Hex:
			if (ch == '(' || ch == '<')
			{
				PushBack(ch);
				return ReadPdfString(out token);
			}

			// Arrays:
			if (ch == '[')
			{
				token = new Token(TokenType.ArrayBegin, "[");
				return true;
			}
			if (ch == ']')
			{
				token = new Token(TokenType.ArrayEnd, "]");
				return true;
			}

			if ((Lexicals.IsAlpha & LexTable[ch]) == Lexicals.IsAlpha)
			{
				PushBack(ch);
				bool ret = ReadIdent(out token);
				if (ret == true)
				{
					if (token.ValueString == "stream")
					{
						return ReadStream(out token);
					}
					else
						return ret;
				}
				return ret;// this would be unexpected.

			}

			throw new UnexpectedTokenException(new string(ch, 1), _inputStream.Position);
		}

		/// <summary>
		/// Reads a PDF stream from the current stream. 
		/// The Ident "stream" should have already been read.
		/// Upon return the ident "endstream" will have already been read.
		/// </summary>
		/// <returns></returns>
		private bool ReadStream(out Token token)
		{
			// read until endstream
			MemoryStream streamBuffer = new MemoryStream();
			byte b;
			byte[] saveBytes = new byte[8];// used to save character sequences that look like "endstream".
			byte[] ndStreamSequence = new byte[8] { (byte)'n', (byte)'d', (byte)'s', (byte)'t', (byte)'r', (byte)'e', (byte)'a', (byte)'m' };

			//FROM PDF SPEC: the stream dictionary must be a direct object. The keyword stream that follows
			//   the stream dictionary should be followed by either a carriage return and a line
			//   feed or by just a line feed, and not by a carriage return alone.
			if (LookAhead('\r'))
				LookAhead('\n');
			LookAhead('\n');

			while (true)
			{
				b = Read();

				if (b != (byte)'e')
					streamBuffer.WriteByte(b);
				else
				{
					if (LookAhead(ndStreamSequence))
					{	// we just read "endstream"
						// if we get here we just read endstream
						token = new Token(TokenType.Stream, streamBuffer);
						return true;
					}
					else
					{
						// write the 'e' to our buffer so we dont' loose it
						streamBuffer.WriteByte((byte)'e');
					}
				}
			}
		}

		/// <summary>
		/// Reads an Ident token from the stream.
		/// </summary>
		/// <param name="token"></param>
		/// <returns></returns>
		private bool ReadIdent(out Token token)
		{
			StringBuilder buffer = new StringBuilder();
			while (true)
			{
				char ch = ReadAsChar();
				if ((LexTable[ch] & Lexicals.IsIdent) == Lexicals.IsIdent)
					buffer.Append(ch);
				else
				{
					PushBack(ch);
					break;
				}
			}
			token = new Token(TokenType.Ident, buffer.ToString());
			return true;
		}

		/// <summary>
		/// Reads a literal string (normal or hex encoded strings are read).
		/// </summary>
		/// <param name="token"></param>
		/// <returns></returns>
		/// <remarks>
		/// the first character should be ( or &lt;.
		/// The string will be unescaped, and it's hex it will be decoded.</remarks>
		private bool ReadPdfString(out Token token)
		{
			StringBuilder buffer = new StringBuilder();
			char ch = ReadAsChar();
			if (ch == '(')
			{
				// read normal string
				while (true)
				{
					ch = ReadAsChar();
					if (ch == ')')
						break;
					if (ch == '\\')
					{
						// next char is an escape:
						ch = ReadPdfStringEscapeChar();
						if (ch != char.MinValue)
							continue;
					}
					buffer.Append(ch);
				}
			}
			else if (ch == '<')
			{
				//read hex string
				while (true)
				{
					char ch1 = ReadAsChar();
					if (ch1 == '>')
						break;
					// check that they're hex digits:
					if ((LexTable[ch1] & Lexicals.IsHexDigit) != Lexicals.IsHexDigit)
						throw new UnexpectedTokenException(new string(ch1, 1), _inputStream.Position);

					char ch2 = ReadAsChar();
					if ((LexTable[ch2] & Lexicals.IsHexDigit) != Lexicals.IsHexDigit)
						throw new UnexpectedTokenException(new string(ch2, 1), _inputStream.Position);
					// convert the hex value to deciaml value and get the char:
					int chVal = ch1 + (ch2 * 16);
					buffer.Append((char)chVal);
				}
			}
			token = new Token(TokenType.PdfString, buffer.ToString());
			return true;
		}

		/// <summary>
		/// Called by ReadString immediately after the escape character (\) is read to return the unescaped character.
		/// </summary>
		/// <returns>char.MinValue if the character should be skipped, otherwise false.</returns>
		private char ReadPdfStringEscapeChar()
		{
			//\n linefeed, \r carriage return, \t horizontal tab, \b backspace, \f formfeed, \\ backslash, \( left parenthesis, \) right parenthesis, \ddd character code ddd (octal)
			char ch = ReadAsChar();
			switch (ch)
			{
				case 'n': return '\n';
				case 'r': return '\r';
				case 't': return '\t';
				case 'b': return '\b';
				case 'f': return '\f';
				case '\\': return '\\';
				case '(': return '(';
				case ')': return '(';
				case (char)0:
					return char.MinValue;
				case '\r':
					return char.MinValue;
				case '\n':
					return char.MinValue;

				default:
					{
						// octal digits?
						if ((LexTable[ch] & Lexicals.IsOctalDigit) == Lexicals.IsOctalDigit)
						{
							int num = 0;
							PushBack(ch);// we'll read this below
							for (int i = 0; i < 3; i++)// max of 3 octal digits:
							{
								string s = new string(ReadAsChar(), 1);

								if ((LexTable[ch] & Lexicals.IsOctalDigit) != Lexicals.IsOctalDigit)
									throw new UnexpectedTokenException("\\" + s, _inputStream.Position);

								num += int.Parse(s) * 8 ^ i;
							}

							return (char)num;
						}
						else
							throw new UnexpectedTokenException("\\" + ch, _inputStream.Position);
					}
			}
		}

		/// <summary>
		/// Reads a number from the stream.
		/// </summary>
		private bool ReadNumber(out Token token)
		{
			StringBuilder buffer = new StringBuilder();
			while (true)
			{
				char ch = ReadAsChar();
				if ((LexTable[ch] & Lexicals.IsNumber) == Lexicals.IsNumber
					|| ch == '-' || ch == '+')
					buffer.Append(ch);
				else if (ch == '.')
					buffer.Append(ch);
				else
				{
					PushBack(ch);
					break;
				}
			}

			token = new Token(TokenType.Number, double.Parse(buffer.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture));
			return true;
		}

		/// <summary>
		///  Reads a PDF name from the current stream. The first / character should have already been read.
		/// </summary>
		/// <returns>True</returns>
		private bool ReadName(out Token token)
		{
			StringBuilder buffer = new StringBuilder();
			bool stop = false;
			while (!stop)
			{
				char ch = ReadAsChar();
				if ((LexTable[ch] & Lexicals.IsName) == Lexicals.IsName)
					buffer.Append(ch);
				else
				{
					PushBack(ch);
					break;
				}
			}
			token = new Token(TokenType.Name, buffer.ToString());
			return true;
		}

		/// <summary>
		/// Gathers all characters from the current position until the next end of line sequence is reached into the specified comment token.
		/// The comment character should have already been read.
		/// </summary>
		/// <param name="token"></param>
		/// <returns></returns>
		private bool ReadComment(out Token token)
		{
			bool stop = false;
			StringBuilder buffer = new StringBuilder();
			try
			{
				while (!stop)
				{
					if (ReadEOL())
						break;

					buffer.Append(ReadAsChar());
				}
			}
			catch (EndOfStreamException)// End of stream is okay, we still return the buffer gathered so far.
			{ }

			token = new Token(TokenType.Comment, buffer.ToString());
			return true;
		}

		/// <summary>
		/// Reads all upcoming whitespace characters.
		/// </summary>
		private void EatWhiteSpace()
		{
			bool stop = false;
			while (!stop)
			{
				char ch = ReadAsChar();
				if ((LexTable[ch] & Lexicals.IsWhiteSpace) == Lexicals.IsWhiteSpace)
					continue;
				else
				{
					this.PushBack(ch);
					break;
				}
			}
		}

		/// <summary>
		/// Looks ahead for an end-of-line sequence. If the next characters are an end of line 
		/// sequence then the EOL characters are read, and true is returned.
		/// If the next characters are not an EOL sequence the characters are not read, and false is returned.
		/// </summary>
		/// <returns>True if an end of line sequence was read, otherwise false.</returns>
		private bool ReadEOL()
		{
			//<end-of-line> ::= <space> <carriage return>
			//| <space> <linefeed>
			//| <carriage return> <linefeed>
			//| <linefeed>
			if (LookAhead(SPACE))
			{
				if (LookAhead(CARRIAGERETURN))
					return true;
				else if (LookAhead(LINEFEED))
					return true;
				else
					PushBack(SPACE);
			}
			else
			{
				if (LookAhead(CARRIAGERETURN))
				{
					if (LookAhead(LINEFEED))
						return true;
					else
						PushBack(CARRIAGERETURN);
				}
				else if (LookAhead(LINEFEED))
				{
					LookAhead(CARRIAGERETURN);
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Builds a table of characters and lexical information about them.
		/// </summary>
		/// <returns></returns>
		private static Lexicals[] CreateLexTable()
		{
			Lexicals[] table = new Lexicals[255];
			// Whitespace:
			table[' '] |= Lexicals.IsWhiteSpace;
			table['\t'] |= Lexicals.IsWhiteSpace;
			table['\r'] |= Lexicals.IsWhiteSpace | Lexicals.IsEOF;
			table['\n'] |= Lexicals.IsWhiteSpace | Lexicals.IsEOF;

			// Name: (section 4.5 Names)
			// A name, like a string, is written as a sequence of characters. It must begin with a
			// slash (/) followed by a sequence of ASCII characters in the range ! (<21>)
			// through ~ (<7E>) except %, (, ), <, >, [, ], {, }, /, and #.
			string nameExluded = "%()<>[]{}/"; // NOTE SOME DISTILLER DOCS USE # as a name character.
			for (char ch = (char)0x21; ch <= 0x7e; ch++)
			{
				if (nameExluded.IndexOf(ch) < 0)
					table[ch] |= Lexicals.IsName;
			}

			// Numbers, Hex and Octal
			for (char ch = (char)'0'; ch <= '9'; ch++)
			{
				if (ch < 9)
					table[ch] |= Lexicals.IsOctalDigit;

				table[ch] |= (Lexicals.IsNumber | Lexicals.IsHexDigit | Lexicals.IsIdent);
			}

			// Characters & More Hex
			for (char ch = (char)'A'; ch <= 'Z'; ch++)
			{
				if (ch >= 'A' && ch <= 'F')
					table[ch] |= Lexicals.IsHexDigit;

				table[ch] |= (Lexicals.IsAlpha | Lexicals.IsIdent);
			}
			for (char ch = (char)'a'; ch <= 'z'; ch++)
			{
				if (ch >= 'a' && ch <= 'f')
					table[ch] |= Lexicals.IsHexDigit;
				table[ch] |= (Lexicals.IsAlpha | Lexicals.IsIdent);
			}

			return table;
		}

		const char COMMENT = '%';
		const char LINEFEED = '\n';
		const char CARRIAGERETURN = '\r';
		const char FORWARDSLASH = '/';
		const char BACKSLASH = '\\';
		const char PARENLEFT = '(';
		const char PARENTRIGHT = ')';
		const char LESSTHAN = '<';
		const char GREATERTHAN = '>';
		const char BRACKETLEFT = '[';
		const char BRACKETRIGHT = ']';
		const char SPACE = ' ';
	}

	public struct Token
	{
		private TokenType _tokenType;
		private double _valueNumber;
		private string _valueString;
		private Stream _valueStream;
		private bool _isEmpty;

		public static readonly Token Empty;

		static Token()
		{
			Empty = new Token();
			Empty._valueNumber = double.NaN;
			Empty._valueString = string.Empty;
			Empty._isEmpty = true;
		}

		public Token(TokenType tokenType, double number)
		{
			if (tokenType != TokenType.Number)
				throw new ArgumentException("Invalid TokenType");

			_tokenType = tokenType;
			_valueNumber = number;
			_valueString = string.Empty;
			_valueStream = null;
			_isEmpty = false;
		}

		public Token(TokenType tokenType, string str)
		{
			if (tokenType == TokenType.Number)
				throw new ArgumentException("Invalid TokenType");

			_tokenType = tokenType;
			_valueString = str;
			_valueNumber = 0;
			_valueStream = null;
			_isEmpty = false;
		}

		public Token(TokenType tokenType, Stream stream)
		{
			if (tokenType != TokenType.Stream)
				throw new ArgumentException("Invalid TokenType");

			_tokenType = tokenType;
			_valueStream = stream;
			_valueNumber = 0;
			_valueString = "";
			_isEmpty = false;
		}

		public TokenType TokenType
		{
			get
			{
				return _tokenType;
			}
		}

		public string ValueString
		{
			get
			{
				if (TokenType == TokenType.Number || TokenType == TokenType.Stream)
					throw new InvalidOperationException("Invalid Token Type");
				return _valueString;
			}
		}

		public double ValueNumber
		{
			get
			{
				if (TokenType != TokenType.Number)
					throw new InvalidOperationException("Invalid Token Type");

				return _valueNumber;
			}
		}


		public Stream ValueStream
		{
			get
			{
				return _valueStream;
			}
		}


		public bool IsEmpty
		{
			get
			{
				return _isEmpty;
			}
		}

		public override bool Equals(object obj)
		{
			if ((obj == null) || (!(obj is Token)))
				return false;

			Token tok = (Token)obj;

			if (tok.TokenType != this.TokenType)
				return false;

			if (tok.TokenType == TokenType.Number)
				return tok.ValueNumber == this.ValueNumber;
			else if (tok.TokenType == TokenType.Stream)
			{
				tok.ValueStream.Seek(0, SeekOrigin.Begin);
				this.ValueStream.Seek(0, SeekOrigin.Begin);
				bool sameLen = tok.ValueStream.Length == this.ValueStream.Length;
				while (this.ValueStream.Position < tok.ValueStream.Length)
				{
					if (this.ValueStream.ReadByte() != tok.ValueStream.ReadByte())
						return false;
				}
				return true;

			}

			return (tok.ValueString == this.ValueString);
		}


		public override int GetHashCode()
		{
			int valueHash = 0;
			if (this.TokenType == TokenType.Number)
				valueHash = this.ValueNumber.GetHashCode();
			else if (this.TokenType == TokenType.Stream)
				valueHash = this.ValueStream.GetHashCode();
			else
				valueHash = this.ValueString.GetHashCode();

			return valueHash % (int)this.TokenType;
		}

		public override string ToString()
		{
			string val = "";
			if (this.TokenType == TokenType.Number)
				val = this.ValueNumber.ToString();
			else if (this.TokenType == TokenType.Stream)
				val = this.ValueStream.Length.ToString();
			else
				val = this.ValueString;

			return string.Concat("Token Type:", this.TokenType.ToString(), " [", val, "]");

		}
	}


	public enum TokenType
	{
		/// Comments begin with a % character and may start at any point on a line.All text between the % character and the end of the line is treated as a comment. Occurrences of the % character within strings or streams are not treated as comments.
		/// // %... &gt;
		Comment,
		//  IDENT (such as obj, endobj, Tj, xref, etc..): [a-zA-Z]*|([a-zA-Z]*[a-zA-Z0-9]*) ??
		Ident,
		/// ([a-zA-Z0-9]*) or &lt;hexchars&gt;
		PdfString,
		/// Name, 
		Name,
		// [0-9]* (could be a decimal/real number too)
		Number,
		/// A symbol such as /, \, (, ), &lt;, &gt;, [, or ].
		ForwardSlash,
		/// &lt;&lt;
		DictionaryBegin,
		/// &gt;&gt;
		DictionaryEnd,
		/// [
		ArrayBegin,
		/// ]
		ArrayEnd,
		/// a PDF stream and the value is the contents between stream and endstream.
		Stream
	}


	/// <summary>
	/// Provides lexical information about characters.
	/// </summary>
	[Flags]
	enum Lexicals : uint
	{
		IsWhiteSpace = 0x001,
		IsName = 0x002,
		IsNumber = 0x004,
		IsAlpha = 0x010,
		IsHexDigit = 0x010,
		IsOctalDigit = 0x020,
		IsIdent = 0x040,
		IsEOF = 0x080
	}


	class UnexpectedTokenException : ApplicationException
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="UnexpectedTokenException"/> class with the specified unexpected text.
		/// </summary>
		/// <param name="unexpectedCharacters">The unexpected character or seuquence of characters.</param>
		public UnexpectedTokenException(string unexpectedCharacters, long streamPosition)
			: base(string.Format("Unexpected token: [{0}] at position :[{1}].", unexpectedCharacters, streamPosition))
		{
		}
	}
}