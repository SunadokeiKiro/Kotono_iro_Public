// Scripts/KukuScripts/WavUtility.cs
using UnityEngine;
using System.Text;
using System.IO;
using System;

/// <summary>
/// UnityでWAVファイルの読み込みと書き出しを行うためのユーティリティクラス。
/// AudioClipとWAV形式のバイト配列を相互に変換します。
/// Version: 1.0 alpha 1 (Modified)
/// Original Source: https://github.com/deadlyfingers/UnityWav
/// </summary>
public static class WavUtility
{
    // 書き出しは16ビットWAVに固定
    private const int BLOCK_SIZE_16_BIT = 2;

    /// <summary>
    /// 指定されたファイルパスからWAVファイルを読み込み、AudioClipに変換します。
    /// Application.persistentDataPath または Application.dataPath 内のパスのみをサポートします。
    /// </summary>
    /// <returns>生成されたAudioClip</returns>
    /// <param name="filePath">.wavファイルへのローカルパス</param>
    public static AudioClip ToAudioClip(string filePath)
    {
        if (!filePath.StartsWith(Application.persistentDataPath) && !filePath.StartsWith(Application.dataPath))
        {
            Debug.LogWarning("This method only supports files stored in Application.persistentDataPath or Application.dataPath.");
            return null;
        }
        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(filePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to read file at {filePath}: {e.Message}");
            return null;
        }
        
        return ToAudioClip(fileBytes, Path.GetFileNameWithoutExtension(filePath));
    }

    /// <summary>
    /// WAVファイルのバイト配列からAudioClipを生成します。
    /// 8, 16, 24, 32ビットの非圧縮PCMフォーマットをサポートします。
    /// </summary>
    /// <param name="fileBytes">WAVファイルのバイト配列</param>
    /// <param name="name">生成するAudioClipの名前</param>
    /// <returns>生成されたAudioClip</returns>
    public static AudioClip ToAudioClip(byte[] fileBytes, string name = "wav")
    {
        // ヘッダから情報を読み取り
        int subchunk1 = BitConverter.ToInt32(fileBytes, 16);
        ushort audioFormat = BitConverter.ToUInt16(fileBytes, 20);

        // PCMフォーマット(1)またはWaveFormatExtensible(65534)のみサポート
        if (audioFormat != 1 && audioFormat != 65534)
        {
            Debug.LogError($"Detected format code '{audioFormat}', but only PCM and WaveFormatExtensible are supported.");
            return null;
        }

        ushort channels = BitConverter.ToUInt16(fileBytes, 22);
        int sampleRate = BitConverter.ToInt32(fileBytes, 24);
        ushort bitDepth = BitConverter.ToUInt16(fileBytes, 34);

        int headerOffset = 16 + 4 + subchunk1 + 4;
        int subchunk2 = BitConverter.ToInt32(fileBytes, headerOffset);

        float[] data;
        switch (bitDepth)
        {
            case 8:
                data = Convert8BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
                break;
            case 16:
                data = Convert16BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
                break;
            case 24:
                data = Convert24BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
                break;
            case 32:
                data = Convert32BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
                break;
            default:
                throw new Exception(bitDepth + " bit depth is not supported.");
        }

        AudioClip audioClip = AudioClip.Create(name, data.Length / channels, channels, sampleRate, false);
        audioClip.SetData(data, 0);
        return audioClip;
    }

    #region WAVからAudioClipへの変換メソッド群
    private static float[] Convert8BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
    {
        int wavSize = BitConverter.ToInt32(source, headerOffset);
        headerOffset += sizeof(int);

        float[] data = new float[wavSize];
        sbyte maxValue = sbyte.MaxValue;

        for (int i = 0; i < wavSize; i++)
        {
            data[i] = (float)source[i + headerOffset] / maxValue;
        }
        return data;
    }

    private static float[] Convert16BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
    {
        int wavSize = BitConverter.ToInt32(source, headerOffset);
        headerOffset += sizeof(int);
        
        int x = sizeof(short);
        int convertedSize = wavSize / x;
        float[] data = new float[convertedSize];
        short maxValue = short.MaxValue;

        for (int i = 0; i < convertedSize; i++)
        {
            int offset = i * x + headerOffset;
            data[i] = (float)BitConverter.ToInt16(source, offset) / maxValue;
        }
        return data;
    }

    private static float[] Convert24BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
    {
        int wavSize = BitConverter.ToInt32(source, headerOffset);
        headerOffset += sizeof(int);

        int x = 3; // 24-bitは3バイト
        int convertedSize = wavSize / x;
        int maxValue = int.MaxValue;
        float[] data = new float[convertedSize];
        byte[] block = new byte[sizeof(int)]; // 4バイトブロックを一時的に使用

        for (int i = 0; i < convertedSize; i++)
        {
            int offset = i * x + headerOffset;
            Buffer.BlockCopy(source, offset, block, 1, x); // 3バイトをオフセット1でコピー
            data[i] = (float)BitConverter.ToInt32(block, 0) / maxValue;
        }
        return data;
    }

    private static float[] Convert32BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
    {
        int wavSize = BitConverter.ToInt32(source, headerOffset);
        headerOffset += sizeof(int);
        
        int x = sizeof(float);
        int convertedSize = wavSize / x;
        int maxValue = int.MaxValue;
        float[] data = new float[convertedSize];

        for (int i = 0; i < convertedSize; i++)
        {
            int offset = i * x + headerOffset;
            data[i] = (float)BitConverter.ToInt32(source, offset) / maxValue;
        }
        return data;
    }
    #endregion

    /// <summary>
    /// AudioClipのデータを16bitのWAV形式のバイト配列に変換します。
    /// </summary>
    /// <param name="audioClip">変換元のAudioClip</param>
    /// <param name="filepath">保存する場合のファイルパス</param>
    /// <param name="saveAsFile">ファイルとして保存するかどうか</param>
    /// <returns>WAV形式のバイト配列</returns>
    public static byte[] FromAudioClip(AudioClip audioClip, string filepath, bool saveAsFile = true)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            const int headerSize = 44;
            ushort bitDepth = 16; // 16-bitに固定

            int fileSize = audioClip.samples * audioClip.channels * BLOCK_SIZE_16_BIT + headerSize;

            // ★★★ エラー修正箇所: refキーワードを削除 ★★★
            WriteFileHeader(stream, fileSize);
            WriteFileFormat(stream, audioClip.channels, audioClip.frequency, bitDepth);
            WriteFileData(stream, audioClip);

            byte[] bytes = stream.ToArray();

            if (saveAsFile)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filepath));
                    File.WriteAllBytes(filepath, bytes);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to write WAV file to {filepath}: {e.Message}");
                }
            }
            return bytes;
        }
    }

    #region AudioClipからWAVへの書き出しメソッド群
    
    // ★★★ エラー修正箇所: refキーワードを削除 ★★★
    private static void WriteFileHeader(MemoryStream stream, int fileSize)
    {
        stream.Write(Encoding.ASCII.GetBytes("RIFF"), 0, 4);
        stream.Write(BitConverter.GetBytes(fileSize - 8), 0, 4);
        stream.Write(Encoding.ASCII.GetBytes("WAVE"), 0, 4);
    }
    
    // ★★★ エラー修正箇所: refキーワードを削除 ★★★
    private static void WriteFileFormat(MemoryStream stream, int channels, int sampleRate, ushort bitDepth)
    {
        stream.Write(Encoding.ASCII.GetBytes("fmt "), 0, 4);
        stream.Write(BitConverter.GetBytes(16), 0, 4); // Sub-chunk size (16 for PCM)
        stream.Write(BitConverter.GetBytes((ushort)1), 0, 2); // Audio format (1 for PCM)
        stream.Write(BitConverter.GetBytes((ushort)channels), 0, 2);
        stream.Write(BitConverter.GetBytes(sampleRate), 0, 4);
        int byteRate = sampleRate * channels * (bitDepth / 8);
        stream.Write(BitConverter.GetBytes(byteRate), 0, 4);
        ushort blockAlign = (ushort)(channels * (bitDepth / 8));
        stream.Write(BitConverter.GetBytes(blockAlign), 0, 2);
        stream.Write(BitConverter.GetBytes(bitDepth), 0, 2);
    }
    
    // ★★★ エラー修正箇所: refキーワードと不要な引数を削除 ★★★
    private static void WriteFileData(MemoryStream stream, AudioClip audioClip)
    {
        float[] data = new float[audioClip.samples * audioClip.channels];
        audioClip.GetData(data, 0);

        byte[] bytes = ConvertAudioClipDataTo16BitByteArray(data);

        stream.Write(Encoding.ASCII.GetBytes("data"), 0, 4);
        stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] ConvertAudioClipDataTo16BitByteArray(float[] data)
    {
        using (MemoryStream dataStream = new MemoryStream())
        {
            short maxValue = short.MaxValue;
            foreach (var sample in data)
            {
                // sampleを-1.0から1.0の範囲にクランプしてから変換
                short intSample = (short)(Mathf.Clamp(sample, -1f, 1f) * maxValue);
                dataStream.Write(BitConverter.GetBytes(intSample), 0, 2);
            }
            return dataStream.ToArray();
        }
    }
    #endregion
}