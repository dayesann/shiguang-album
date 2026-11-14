# Original procedural composition, no sampled or third-party audio.
$ErrorActionPreference = 'Stop'
$taskMusicFolder = Join-Path $PSScriptRoot '..\src\Assets\Music'
New-Item -ItemType Directory -Path $taskMusicFolder -Force | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class ShiguangMusicComposer {
    public static void Write(string path) {
        const int rate = 22050, seconds = 48;
        var samples = new double[rate * seconds];
        int[][] chords = { new[]{60,64,67,71}, new[]{57,60,64,67}, new[]{53,57,60,64}, new[]{55,59,62,67} };
        int[] melody = { 76,79,74,72, 76,72,71,67, 69,72,76,74, 71,74,79,74 };
        void Note(double start, double duration, int midi, double gain) {
            double hz = 440 * Math.Pow(2, (midi-69)/12.0);
            for (int j = 0; j < duration * rate && (int)(start*rate)+j < samples.Length; j++) {
                double t = j/(double)rate;
                double envelope = (1-Math.Exp(-t*12)) * Math.Exp(-t*1.05) * Math.Min(1, (duration-t)/.3);
                double phase = 2*Math.PI*hz*t;
                samples[(int)(start*rate)+j] += gain * envelope * (Math.Sin(phase)+.18*Math.Sin(phase*2)+.04*Math.Sin(phase*3));
            }
        }
        for (int bar = 0; bar < 12; bar++) {
            var chord = chords[bar%4];
            for (int beat = 0; beat < 4; beat++) Note(bar*4+beat*.85, 3.2, chord[beat]+12, .10);
            Note(bar*4, 3.8, chord[0]-12, .11);
            Note(bar*4+.4, 3.1, melody[(bar*2)%16], .075);
            Note(bar*4+2.4, 3.1, melody[(bar*2+1)%16], .065);
        }
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36+samples.Length*2);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate*2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length*2);
        for(int i=0; i<samples.Length; i++) {
            double fade = Math.Min(1, Math.Min(i/(double)rate, (samples.Length-1-i)/(rate*2.0)));
            writer.Write((short)(Math.Clamp(samples[i]*fade, -.9, .9)*32767));
        }
    }
}
'@
[ShiguangMusicComposer]::Write((Join-Path $taskMusicFolder 'shiguang.wav'))
