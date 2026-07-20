using System.Text;

namespace Jellyfin.Plugin.AniDL.Sources.AniSuge;

public static class AniSugeVrf
{
    private const string Key = "ysJhV6U27FVIjjuk";
    private static readonly int[] Shifts = [-3, 3, -4, 2, -2, 5, 4, 5];

    public static string Create(string value)
    {
        var encoded = Uri.EscapeDataString(value);
        var encrypted = Rc4(Key, Encoding.UTF8.GetBytes(encoded));
        var first = ToUrlBase64(encrypted);
        var shifted = first.Select((character, index) => (byte)(character + Shifts[index % Shifts.Length])).ToArray();
        return Rot13(ToUrlBase64(shifted));
    }

    private static byte[] Rc4(string key, byte[] input)
    {
        var state = Enumerable.Range(0, 256).ToArray();
        var j = 0;
        for (var i = 0; i < state.Length; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 255;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[input.Length];
        int x = 0;
        j = 0;
        for (var index = 0; index < input.Length; index++)
        {
            x = (x + 1) & 255;
            j = (j + state[x]) & 255;
            (state[x], state[j]) = (state[j], state[x]);
            output[index] = (byte)(input[index] ^ state[(state[x] + state[j]) & 255]);
        }

        return output;
    }

    private static string ToUrlBase64(byte[] bytes) => Convert.ToBase64String(bytes).Replace('/', '_').Replace('+', '-');

    private static string Rot13(string value) => string.Create(value.Length, value, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            span[i] = ch switch
            {
                >= 'a' and <= 'z' => (char)('a' + ((ch - 'a' + 13) % 26)),
                >= 'A' and <= 'Z' => (char)('A' + ((ch - 'A' + 13) % 26)),
                _ => ch
            };
        }
    });
}

