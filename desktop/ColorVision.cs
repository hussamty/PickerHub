namespace PickerHub;

// Machado et al. (2009), severity 1.0. Apply in linear sRGB, then re-encode.
// Reference: https://www.inf.ufrgs.br/~oliveira/pubs_files/CVD_Simulation/CVD_Simulation.html
// Matrices also documented by QGIS: https://api.qgis.org/api/master/qgsprevieweffect_8cpp_source.html
internal static class ColorVision
{
    static readonly double[][] Matrices =
    [
        [.152286,1.052583,-.204868, .114503,.786281,.099216, -.003882,-.048116,1.051998],
        [.367322,.860646,-.227968, .280085,.672501,.047413, -.011820,.042940,.968881],
        [1.255528,-.076749,-.178779, -.078411,.930809,.147602, .004733,.691367,.303900]
    ];
    public static string Simulate(string hex, int kind)
    {
        if (!Workspace.Hex(hex) || kind < 0 || kind >= Matrices.Length) throw new ArgumentException("Invalid color vision preview");
        var channels = Enumerable.Range(0, 3).Select(i => Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255d)
            .Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
        var matrix = Matrices[kind]; var result = new int[3];
        for (int i = 0; i < 3; i++)
        {
            double v = Math.Clamp(matrix[i * 3] * channels[0] + matrix[i * 3 + 1] * channels[1] + matrix[i * 3 + 2] * channels[2], 0, 1);
            result[i] = (int)Math.Round(255 * (v <= .0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - .055));
        }
        return $"#{result[0]:X2}{result[1]:X2}{result[2]:X2}";
    }
}
