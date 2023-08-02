﻿using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using OneOf;

namespace LucHeart.CoreOSC;

public abstract class OscPacket
{
    public static OneOf<OscMessage, OscBundle> GetPacket(byte[] oscData)
    {
        if (oscData[0] == '#')
            return ParseBundle(oscData);
        return ParseMessage(oscData);
    }

    public abstract byte[] GetBytes();

    #region Parse OSC packages

    /// <summary>
    /// Takes in an OSC bundle package in byte form and parses it into a more usable OscBundle object
    /// </summary>
    /// <param name="msg"></param>
    /// <returns>Message containing various arguments and an address</returns>
    private static OscMessage ParseMessage(byte[] msg)
    {
        ReadOnlySpan<byte> msgSpan = msg.AsSpan();
        var index = 0;

        var arguments = new List<object?>();
        var mainArray = arguments; // used as a reference when we are parsing arrays to get the main array back

        // Get address
        var address = getAddress(msg, index);
        index += msg.FirstIndexAfter(address.Length, x => x == ',');

        if (index % 4 != 0)
            throw new Exception(
                "Misaligned OSC Packet data. Address string is not padded correctly and does not align to 4 byte interval");

        // Get type tags
        var types = getTypes(msg, index);
        index += types.Length;

        while (index % 4 != 0)
            index++;

        var commaParsed = false;

        foreach (var type in types)
        {
            // skip leading comma
            if (type == ',' && !commaParsed)
            {
                commaParsed = true;
                continue;
            }

            switch (type)
            {
                case '\0':
                    break;

                case 'i':
                    var intVal = getInt(msgSpan, index);
                    arguments.Add(intVal);
                    index += 4;
                    break;

                case 'f':
                    var floatVal = getFloat(msgSpan, index);
                    arguments.Add(floatVal);
                    index += 4;
                    break;

                case 's':
                    var stringVal = getString(msg, index);
                    arguments.Add(stringVal);
                    index += Encoding.UTF8.GetBytes(stringVal).Length;
                    break;

                case 'b':
                    var blob = getBlob(msgSpan, index);
                    arguments.Add(blob.ToArray());
                    index += 4 + blob.Length;
                    break;

                case 'h':
                    var hval = getLong(msgSpan, index);
                    arguments.Add(hval);
                    index += 8;
                    break;

                case 't':
                    var sval = getULong(msgSpan, index);
                    arguments.Add(new Timetag(sval));
                    index += 8;
                    break;

                case 'd':
                    var dval = getDouble(msg, index);
                    arguments.Add(dval);
                    index += 8;
                    break;

                case 'S':
                    var symbolVal = getString(msg, index);
                    arguments.Add(new Symbol(symbolVal));
                    index += symbolVal.Length;
                    break;

                case 'c':
                    var cval = getChar(msg, index);
                    arguments.Add(cval);
                    index += 4;
                    break;

                case 'r':
                    var rgbaval = getRGBA(msg, index);
                    arguments.Add(rgbaval);
                    index += 4;
                    break;

                case 'm':
                    var midival = getMidi(msg, index);
                    arguments.Add(midival);
                    index += 4;
                    break;

                case 'T':
                    arguments.Add(true);
                    break;

                case 'F':
                    arguments.Add(false);
                    break;

                case 'N':
                    arguments.Add(null);
                    break;

                case 'I':
                    arguments.Add(double.PositiveInfinity);
                    break;

                case '[':
                    if (arguments != mainArray)
                        throw new Exception("CoreOSC does not support nested arrays");
                    arguments = new List<object?>(); // make arguments point to a new object array
                    break;

                case ']':
                    mainArray.Add(arguments); // add the array to the main array
                    arguments = mainArray; // make arguments point back to the main array
                    break;

                default:
                    throw new Exception("OSC type tag '" + type + "' is unknown.");
            }

            while (index % 4 != 0)
                index++;
        }

        return new OscMessage(address, arguments.ToArray());
    }

    /// <summary>
    /// Takes in an OSC bundle package in byte form and parses it into a more usable OscBundle object
    /// </summary>
    /// <param name="bundle"></param>
    /// <returns>Bundle containing elements and a timetag</returns>
    private static OscBundle ParseBundle(byte[] bundle)
    {
        ulong timetag;
        var messages = new List<OscMessage>();

        int index = 0;

        var bundleTag = Encoding.ASCII.GetString(bundle[..8]);
        index += 8;

        timetag = getULong(bundle, index);
        index += 8;

        if (bundleTag != "#bundle\0")
            throw new Exception("Not a bundle");

        while (index < bundle.Length)
        {
            var size = getInt(bundle, index);
            index += 4;

            var messageBytes = bundle[index..(index + size)];
            var message = ParseMessage(messageBytes);

            messages.Add(message);

            index += size;
            while (index % 4 != 0)
                index++;
        }

        var output = new OscBundle(timetag, messages.ToArray());
        return output;
    }

    #endregion Parse OSC packages

    #region Get arguments from byte array

    private static string getAddress(byte[] msg, int index)
    {
        var address = "";
        var chars = Encoding.UTF8.GetChars(msg);

        for (var i = index; i < chars.Length; i++)
        {
            if (chars[i] != ',') continue;
            address = string.Join("", chars[index..i]);
            break;
        }

        return address.Replace("\0", "");
    }

    private static char[] getTypes(byte[] msg, int index)
    {
        var i = index + 4;
        char[] types = null;

        for (; i <= msg.Length; i += 4)
        {
            if (msg[i - 1] == 0)
            {
                types = Encoding.ASCII.GetChars(msg[index..i]);
                break;
            }
        }

        if (i >= msg.Length && types == null)
            throw new Exception("No null terminator after type string");

        return types;
    }

    private static int getInt(ReadOnlySpan<byte> msg, int index) =>
        BinaryPrimitives.ReadInt32BigEndian(msg.Slice(index, 4));


    private static float getFloat(ReadOnlySpan<byte> msg, int index)
    {
        byte[] reversed = new byte[4];
        reversed[3] = msg[index];
        reversed[2] = msg[index + 1];
        reversed[1] = msg[index + 2];
        reversed[0] = msg[index + 3];
        float val = BitConverter.ToSingle(reversed, 0);
        return val;
    }

    private static string getString(byte[] msg, int index)
    {
        string? output = null;
        var i = index + 4;
        for (; i - 1 < msg.Length; i += 4)
        {
            if (msg[i - 1] != 0) continue;
            output = Encoding.UTF8.GetString(msg[index..i]);
            break;
        }

        if (i >= msg.Length && output == null)
            throw new Exception("No null terminator after type string");

        return output?.Replace("\0", "") ?? string.Empty;
    }

    private static ReadOnlySpan<byte> getBlob(ReadOnlySpan<byte> msg, int index)
    {
        var size = getInt(msg, index);
        return msg[(index + 4)..(index + 4 + size)];
    }

    private static ulong getULong(ReadOnlySpan<byte> msg, int index) => BinaryPrimitives.ReadUInt64BigEndian(msg.Slice(index, 8));
    private static long getLong(ReadOnlySpan<byte> msg, int index) => BinaryPrimitives.ReadInt64BigEndian(msg.Slice(index, 8));

    private static double getDouble(byte[] msg, int index)
    {
        byte[] var = new byte[8];
        var[7] = msg[index];
        var[6] = msg[index + 1];
        var[5] = msg[index + 2];
        var[4] = msg[index + 3];
        var[3] = msg[index + 4];
        var[2] = msg[index + 5];
        var[1] = msg[index + 6];
        var[0] = msg[index + 7];

        double val = BitConverter.ToDouble(var, 0);
        return val;
    }

    private static char getChar(byte[] msg, int index)
    {
        return (char)msg[index + 3];
    }

    private static RGBA getRGBA(byte[] msg, int index)
    {
        return new RGBA(msg[index], msg[index + 1], msg[index + 2], msg[index + 3]);
    }

    private static Midi getMidi(byte[] msg, int index)
    {
        return new Midi(msg[index], msg[index + 1], msg[index + 2], msg[index + 3]);
    }

    #endregion Get arguments from byte array

    #region Create byte arrays for arguments

    protected static byte[] setInt(int value)
    {
        byte[] msg = new byte[4];

        var bytes = BitConverter.GetBytes(value);
        msg[0] = bytes[3];
        msg[1] = bytes[2];
        msg[2] = bytes[1];
        msg[3] = bytes[0];

        return msg;
    }

    protected static byte[] setFloat(float value)
    {
        byte[] msg = new byte[4];

        var bytes = BitConverter.GetBytes(value);
        msg[0] = bytes[3];
        msg[1] = bytes[2];
        msg[2] = bytes[1];
        msg[3] = bytes[0];

        return msg;
    }

    protected static byte[] setString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var msg = new byte[(bytes.Length / 4 + 1) * 4];
        bytes.CopyTo(msg, 0);
        return msg;
    }

    protected static byte[] setBlob(byte[] value)
    {
        int len = value.Length + 4;
        len = len + (4 - len % 4);

        byte[] msg = new byte[len];
        byte[] size = setInt(value.Length);
        size.CopyTo(msg, 0);
        value.CopyTo(msg, 4);
        return msg;
    }

    protected static byte[] setLong(Int64 value)
    {
        byte[] rev = BitConverter.GetBytes(value);
        byte[] output = new byte[8];
        output[0] = rev[7];
        output[1] = rev[6];
        output[2] = rev[5];
        output[3] = rev[4];
        output[4] = rev[3];
        output[5] = rev[2];
        output[6] = rev[1];
        output[7] = rev[0];
        return output;
    }

    protected static byte[] setULong(UInt64 value)
    {
        byte[] rev = BitConverter.GetBytes(value);
        byte[] output = new byte[8];
        output[0] = rev[7];
        output[1] = rev[6];
        output[2] = rev[5];
        output[3] = rev[4];
        output[4] = rev[3];
        output[5] = rev[2];
        output[6] = rev[1];
        output[7] = rev[0];
        return output;
    }

    protected static byte[] setDouble(double value)
    {
        byte[] rev = BitConverter.GetBytes(value);
        byte[] output = new byte[8];
        output[0] = rev[7];
        output[1] = rev[6];
        output[2] = rev[5];
        output[3] = rev[4];
        output[4] = rev[3];
        output[5] = rev[2];
        output[6] = rev[1];
        output[7] = rev[0];
        return output;
    }

    protected static byte[] setChar(char value)
    {
        byte[] output = new byte[4];
        output[0] = 0;
        output[1] = 0;
        output[2] = 0;
        output[3] = (byte)value;
        return output;
    }

    #endregion Create byte arrays for arguments
}