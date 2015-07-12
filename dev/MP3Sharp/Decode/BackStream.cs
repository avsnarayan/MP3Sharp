// /***************************************************************************
//  * BackStream.cs
//  * Copyright (c) 2015 the authors.
//  * 
//  * All rights reserved. This program and the accompanying materials
//  * are made available under the terms of the GNU Lesser General Public License
//  * (LGPL) version 3 which accompanies this distribution, and is available at
//  * https://www.gnu.org/licenses/lgpl-3.0.en.html
//  *
//  * This library is distributed in the hope that it will be useful,
//  * but WITHOUT ANY WARRANTY; without even the implied warranty of
//  * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  * Lesser General Public License for more details.
//  *
//  ***************************************************************************/

using System;
using System.IO;

namespace MP3Sharp.Decode
{
    [Serializable]
    internal class CircularByteBuffer
    {
        private byte[] dataArray;
        private int index;
        private int length = 1;
        private int numValid;

        public CircularByteBuffer(int size)
        {
            dataArray = new byte[size];
            length = size;
        }

        /// <summary>
        ///     Initialize by copying the CircularByteBuffer passed in
        /// </summary>
        public CircularByteBuffer(CircularByteBuffer cdb)
        {
            lock (cdb)
            {
                length = cdb.length;
                numValid = cdb.numValid;
                index = cdb.index;
                dataArray = new byte[length];
                for (int c = 0; c < length; c++)
                {
                    dataArray[c] = cdb.dataArray[c];
                }
            }
        }

        /// <summary>
        ///     The physical size of the Buffer (read/write)
        /// </summary>
        public int BufferSize
        {
            get { return length; }
            set
            {
                byte[] newDataArray = new byte[value];

                int minLength = (length > value) ? value : length;
                for (int i = 0; i < minLength; i++)
                {
                    newDataArray[i] = InternalGet(i - length + 1);
                }
                dataArray = newDataArray;
                index = minLength - 1;
                length = value;
            }
        }

        /// <summary>
        ///     e.g. Offset[0] is the current value
        /// </summary>
        public byte this[int index]
        {
            get { return InternalGet(-1 - index); }
            set { InternalSet(-1 - index, value); }
        }

        /// <summary>
        ///     How far back it is safe to look (read/write).  Write only to reduce NumValid.
        /// </summary>
        public int NumValid
        {
            get { return numValid; }
            set
            {
                if (value > numValid)
                    throw new Exception("Can't set NumValid to " + value +
                                        " which is greater than the current numValid value of " + numValid);
                numValid = value;
            }
        }

        public CircularByteBuffer Copy()
        {
            return new CircularByteBuffer(this);
        }

        public void Reset()
        {
            index = 0;
            numValid = 0;
        }

        /// <summary>
        ///     Push a byte into the buffer.  Returns the value of whatever comes off.
        /// </summary>
        public byte Push(byte newValue)
        {
            byte ret;
            lock (this)
            {
                ret = InternalGet(length);
                dataArray[index] = newValue;
                numValid++;
                if (numValid > length) numValid = length;
                index++;
                index %= length;
            }
            return ret;
        }

        /// <summary>
        ///     Pop an integer off the start of the buffer. Throws an exception if the buffer is empty (NumValid == 0)
        /// </summary>
        public byte Pop()
        {
            lock (this)
            {
                if (numValid == 0) throw new Exception("Can't pop off an empty CircularByteBuffer");
                numValid--;
                return this[numValid];
            }
        }

        /// <summary>
        ///     Returns what would fall out of the buffer on a Push.  NOT the same as what you'd get with a Pop().
        /// </summary>
        public byte Peek()
        {
            lock (this)
            {
                return InternalGet(length);
            }
        }

        private byte InternalGet(int offset)
        {
            int ind = index + offset;

            // Do thin modulo (should just drop through)
            for (; ind >= length; ind -= length) ;
            for (; ind < 0; ind += length) ;
            // Set value
            return dataArray[ind];
        }

        private void InternalSet(int offset, byte valueToSet)
        {
            int ind = index + offset;

            // Do thin modulo (should just drop through)
            for (; ind > length; ind -= length) ;
            for (; ind < 0; ind += length) ;
            // Set value
            dataArray[ind] = valueToSet;
        }

        /// <summary>
        ///     Returns a range (in terms of Offsets) in an int array in chronological (oldest-to-newest) order. e.g. (3, 0)
        ///     returns the last four ints pushed, with result[3] being the most recent.
        /// </summary>
        public byte[] GetRange(int str, int stp)
        {
            byte[] outByte = new byte[str - stp + 1];

            for (int i = str, j = 0; i >= stp; i--,j++)
            {
                outByte[j] = this[i];
            }

            return outByte;
        }

        public override string ToString()
        {
            string ret = "";
            for (int i = 0; i < dataArray.Length; i++)
            {
                ret += dataArray[i] + " ";
            }
            ret += "\n index = " + index + " numValid = " + NumValid;
            return ret;
        }
    }

    internal class BackStream
    {
        private readonly int BackBufferSize;
        private readonly CircularByteBuffer COB;
        private readonly Stream S;
        private readonly byte[] Temp;
        private int NumForwardBytesInBuffer;

        public BackStream(Stream s, int backBufferSize)
        {
            S = s;
            BackBufferSize = backBufferSize;
            Temp = new byte[BackBufferSize];
            COB = new CircularByteBuffer(BackBufferSize);
        }

        public int Read(sbyte[] toRead, int offset, int length)
        {
            // Read 
            int currentByte = 0;
            bool canReadStream = true;
            while (currentByte < length && canReadStream)
            {
                if (NumForwardBytesInBuffer > 0)
                {
                    // from mem
                    NumForwardBytesInBuffer--;
                    toRead[offset + currentByte] = (sbyte) COB[NumForwardBytesInBuffer];
                    currentByte++;
                }
                else
                {
                    // from stream
                    int newBytes = length - currentByte;
                    int numRead = S.Read(Temp, 0, newBytes);
                    canReadStream = numRead >= newBytes;
                    for (int i = 0; i < numRead; i++)
                    {
                        COB.Push(Temp[i]);
                        toRead[offset + currentByte + i] = (sbyte) Temp[i];
                    }
                    currentByte += numRead;
                }
            }
            return currentByte;
        }

        public void UnRead(int length)
        {
            NumForwardBytesInBuffer += length;
            if (NumForwardBytesInBuffer > BackBufferSize)
            {
                Console.WriteLine("YOUR BACKSTREAM IS FISTED!");
            }
        }

        public void Close()
        {
            S.Close();
        }
    }
}