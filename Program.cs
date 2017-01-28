﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace Mandelbrot
{
    internal class Display
    {
        private Bitmap _bmp;
        public static void Main(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));

            var tt = new List<Tuple<double, Color>>
            {
                new Tuple<double, Color>(0.0, Color.FromArgb(255, 0, 7, 100)),
                new Tuple<double, Color>(0.16, Color.FromArgb(255, 32, 107, 203)),
                new Tuple<double, Color>(0.42, Color.FromArgb(255, 237, 255, 255)),
                new Tuple<double, Color>(0.6425, Color.FromArgb(255, 255, 170, 0)),
                new Tuple<double, Color>(0.8575, Color.FromArgb(255, 0, 2, 0)),
                new Tuple<double, Color>(1.0, Color.FromArgb(255, 0, 7, 100))
            };
            int width = 1050, height = 600;
            if (args.Length > 0)
            {
                width = int.Parse(args[0]);
                height = int.Parse(args[1]);
            }
            var gradient = new Program.Gradient(256, 0);
            var palette = Palette.GenerateColorPalette(tt,768);
            var display = new Display
            {
                _bmp = Program.DrawMandelbrot(new Size(width, height),
                    new Program.Region(new Complex(-2.5, -1), new Complex(1, 1)), 1000, palette, gradient, 1E10)
            };
            var p = new PictureBox {Size = display._bmp.Size};
            var form = new Form
            {
                Name = "Mandelbrot Display",
                Visible = false,
                AutoSize = true,
                Size = display._bmp.Size,
                KeyPreview = true
            };
            form.KeyPress += display.Form_KeyPress;
            form.KeyDown += display.Form_KeyDown;
            EventHandler resizeHandler = (sender, e) =>
            {
                var size = form.Size;
                form.Visible = false;
                form.Close();
                Main(new[] {size.Width.ToString(), size.Height.ToString()});
            };
            form.ResizeEnd += resizeHandler;
            p.KeyPress += display.Form_KeyPress;
            p.KeyDown += display.Form_KeyDown;
            p.Image = display._bmp;
            form.Controls.Add(p);
            form.ShowDialog();
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.S) return;
            SaveBitmap();
            e.Handled = true;
        }
        private void Form_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar != 'S') return;
            SaveBitmap();
            e.Handled = true;
        }

        private void SaveBitmap()
        {
            _bmp.Save("Image.png", ImageFormat.Png);
        }
    }

    public static class MathUtils
    {
        public enum ExtrapolationType
        {
            Linear,
            Constant,
            None
        }

        public const double Tolerance = 1E-15;

        public static Func<double, double> CreateInterpolant(double[] xs, double[] ys,
            ExtrapolationType extrapolationType)
        {
            var length = xs.Length;
            if (length != ys.Length)
            {
                throw new ArgumentException(
                    $"length of xs ({length}) must be equal to length of ys ({ys.Length})");
            }
            if (length == 0)
            {
                return x => 0.0;
            }
            if (length == 1)
            {
                return x => ys[0];
            }
            var indices = Enumerable.Range(0, length).ToList();
            indices.Sort((a, b) => xs[a] < xs[b] ? -1 : 1);
            double[] newXs = new double[xs.Length], newYs = new double[ys.Length];
            for (var i = 0; i < length; i++)
            {
                newXs[i] = xs[indices[i]];
                newYs[i] = ys[indices[i]];
            }
            // Get consecutive differences and slopes
            double[] dxs = new double[length - 1], ms = new double[length - 1];
            for (var i = 0; i < length - 1; i++)
            {
                double dx = newXs[i + 1] - newXs[i], dy = newYs[i + 1] - newYs[i];
                dxs[i] = dx;
                ms[i] = dy/dx;
            }
            // Get degree-1 coefficients
            var c1S = new double[dxs.Length + 1];
            c1S[0] = ms[0];
            for (var i = 0; i < dxs.Length - 1; i++)
            {
                double m = ms[i], mNext = ms[i + 1];
                if (m*mNext <= 0)
                {
                    c1S[i + 1] = 0;
                }
                else
                {
                    double dx = dxs[i], dxNext = dxs[i + 1], common = dx + dxNext;
                    c1S[i + 1] = (3*common/((common + dxNext)/m + (common + dx)/mNext));
                }
            }
            c1S[c1S.Length - 1] = (ms[ms.Length - 1]);
            // Get degree-2 and degree-3 coefficients
            double[] c2S = new double[c1S.Length - 1], c3S = new double[c1S.Length - 1];
            for (var i = 0; i < c1S.Length - 1; i++)
            {
                double c1 = c1S[i], m = ms[i], invDx = 1/dxs[i], common = c1 + c1S[i + 1] - m - m;
                c2S[i] = (m - c1 - common)*invDx;
                c3S[i] = common*invDx*invDx;
            }
            // Return interpolant function
            Func<double, double> interpolant = x =>
            {
                // The rightmost point in the dataset should give an exact result
                var i = newXs.Length - 1;
                if (Math.Abs(x - newXs[i]) < Tolerance)
                {
                    return newYs[i];
                }
                // Search for the interval x is in, returning the corresponding y if x is one of the original newXs
                var low = 0;
                var high = c3S.Length - 1;
                while (low <= high)
                {
                    var mid = low + ((high - low)/2);
                    var xHere = newXs[mid];
                    if (xHere < x)
                    {
                        low = mid + 1;
                    }
                    else if (xHere > x)
                    {
                        high = mid - 1;
                    }
                    else
                    {
                        return newYs[mid];
                    }
                }
                i = Math.Max(0, high);
                // Interpolate
                double diff = x - newXs[i], diffSq = diff*diff;
                return newYs[i] + c1S[i]*diff + c2S[i]*diffSq + c3S[i]*diff*diffSq;
            };
            double xMin = newXs.Min(),
                xMax = newXs.Max(),
                yMin = newYs.Min(),
                yMax = newYs.Max();
            return x =>
            {
                if (x >= xMin && x <= xMax)
                {
                    return interpolant(x);
                }
                switch (extrapolationType)
                {
                    case ExtrapolationType.None:
                        return interpolant(x);
                    case ExtrapolationType.Linear:
                        return yMin + (x - xMin)/(xMax - xMin)*yMax;
                    case ExtrapolationType.Constant:
                        return (x < xMin) ? yMin : yMax;
                    default:
                        return double.NaN;
                }
            };
        }

        public static double Clamp(double val, double min, double max)
        {
            return (val < min) ? min : ((val > max) ? max : val);
        }
    }

    public static class Palette
    {
        public static List<Color> GenerateColorPalette(List<Tuple<double, Color>> controls, int numColors)
        {
            var palette = new Color[numColors];

            double[] red = controls.Select(x => x.Item2.R/255.0).ToArray(),
                green = controls.Select(x => x.Item2.G/255.0).ToArray(),
                blue = controls.Select(x => x.Item2.B/255.0).ToArray(),
                xs = controls.Select(x => x.Item1).ToArray();
            var channelSplines = new[]
            {
                MathUtils.CreateInterpolant(xs, red, MathUtils.ExtrapolationType.None),
                MathUtils.CreateInterpolant(xs, green, MathUtils.ExtrapolationType.None),
                MathUtils.CreateInterpolant(xs, blue, MathUtils.ExtrapolationType.None)
            };
            Enumerable.Range(0, palette.Length).ToList().ForEach(i => palette[i] = (Color.FromArgb(
                (int) MathUtils.Clamp(Math.Abs((channelSplines[0]((double) i/palette.Count())*255.0)), 0.0, 255.0),
                (int) MathUtils.Clamp(Math.Abs((channelSplines[1]((double) i/palette.Count())*255.0)), 0.0, 255.0),
                (int) MathUtils.Clamp(Math.Abs((channelSplines[2]((double) i/palette.Count())*255.0)), 0.0, 255.0)
                )));
            return palette.ToList();
        }

        public static Color Lerp(Color fromColor, Color toColor, double bias)
        {
            bias = (double.IsNaN(bias)) ? 0 : (double.IsInfinity(bias) ? 1 : bias);
            return Color.FromArgb(
                (int) (fromColor.R*(1.0f - bias) + toColor.R*(bias)),
                (int) (fromColor.G*(1.0f - bias) + toColor.G*(bias)),
                (int) (fromColor.B*(1.0f - bias) + toColor.B*(bias))
                );
        }

        public static Bitmap PaletteDebug(int height, int width, List<Color> palette)
        {
            var paletteLength = palette.Count();
            width = Math.Min(width, paletteLength);
            var img = new Bitmap(width, height);
            var paletteStep = paletteLength/width;
            for (var y = 0; y < img.Height; y++)
            {
                var paletteIndex = 0;
                for (var x = 0; x < img.Width; x++)
                {
                    paletteIndex += paletteStep;
                    if (paletteIndex >= paletteLength)
                    {
                        break;
                    }
                    img.SetPixel(x, y, palette[paletteIndex]);
                }
            }
            return img;
        }
    }

    internal static class Program
    {
        private static readonly double OneOverLog2 = 1/Math.Log(2);

        public static Bitmap DrawMandelbrot(Size size, Region region, int maxIteration, List<Color> palette,
            Gradient gradient, double bailout)
        {
            region = NormalizeRegion(region);
            Complex max = region.Max, min = region.Min;
            double rMin = min.Real, rMax = max.Real, iMax = max.Imaginary, iMin = min.Imaginary;
            return DrawMandelbrot(size.Width, size.Height, rMin, rMax, iMin, iMax, maxIteration, palette, gradient,
                bailout);
        }

        private static Bitmap DrawMandelbrot(int width, int height, double rMin, double rMax, double iMin, double iMax,
            int maxIteration,
            IReadOnlyList<Color> palette, Gradient gradient, double bailout)
        {
            var img = new Bitmap(width, height); // Bitmap to contain the set

            var rScale = (Math.Abs(rMin) + Math.Abs(rMax))/width; // Amount to move each pixel in the real numbers
            var iScale = (Math.Abs(iMin) + Math.Abs(iMax))/height; // Amount to move each pixel in the imaginary numbers
            var logBailout = Math.Log(bailout);
            var bailoutSquared = bailout*bailout;
            for (var px = 0; px < width; px++)
            {
                for (var py = 0; py < height; py++)
                {
                    var x0 = px*rScale + rMin;
                    var y0 = py*iScale + iMin;
                    double x = 0.0, xp = 0.0; // x;
                    double y = 0.0, yp = 0.0; // y;
                    var mod = 0.0; // x * x + y * y;
                    var iteration = 0;
                    while (mod < bailoutSquared && iteration < maxIteration)
                    {
                        var xtemp = x*x - y*y + x0;
                        var ytemp = 2*x*y + y0;
                        if ((Math.Abs(xtemp - x) < MathUtils.Tolerance && Math.Abs(ytemp - y) < MathUtils.Tolerance) ||
                            (Math.Abs(xtemp - xp) < MathUtils.Tolerance && Math.Abs(ytemp - yp) < MathUtils.Tolerance))
                        {
                            iteration = maxIteration;
                            break;
                        }
                        xp = x;
                        yp = y;
                        x = xtemp;
                        y = ytemp;
                        mod = x*x + y*y;
                        ++iteration;
                    }
                    var size = Math.Sqrt(mod);
                    var smoothed = Math.Log(Math.Log(size)*logBailout)*OneOverLog2;
                    var bias = smoothed - (long) smoothed;
                    var idx = (Math.Sqrt(iteration + 1 - smoothed)*gradient.Scale + gradient.Shift)%palette.Count;
                    idx = NormalizeIdx(idx, palette.Count);
                    if (gradient.Logindex)
                    {
                        idx = ((Math.Log(idx)/Math.Log(palette.Count))*gradient.Scale)%palette.Count;
                        idx = NormalizeIdx(idx, palette.Count);
                    }
                    var color = Palette.Lerp(palette[(int) idx], palette[((int) idx + 1)%palette.Count], bias);
                    img.SetPixel(px, py, color);
                }
            }
            return img;
        }

        private static Region NormalizeRegion(Region region)
        {
            return region.OriginAndWidth ? new Region(OriginAndWidthToRegion(region)) : region;
        }

        private static Tuple<Complex, Complex> OriginAndWidthToRegion(Region region)
        {
            var origin = region.Min;
            var halfX = region.Max.Real/2;
            var halfY = region.Max.Imaginary/2;
            return new Tuple<Complex, Complex>(
                new Complex(origin.Real - halfX, origin.Imaginary - halfY),
                new Complex(origin.Real + halfX, origin.Imaginary + halfY));
        }

        private static double NormalizeIdx(double idx, double max)
        {
            return (idx < 0)
                ? Math.Abs((max + idx))%max
                : (double.IsNaN(idx) ? 0 : (double.IsInfinity(idx) ? max : idx));
        }

        public struct Region

        {
            public Region(Tuple<Complex, Complex> val) : this(val.Item1, val.Item2)
            {
            }

            public Region(Complex min, Complex max, bool originAndWidth = false)
            {
                Max = max;
                Min = min;
                OriginAndWidth = originAndWidth;
            }

            public Complex Min { get; }
            public Complex Max { get; }
            public bool OriginAndWidth { get; }



       public static Region FromString(string load)
            {
                var data = load.Split(' ');
                var min = data[0].Split(',');
                var max = data[1].Split(',');
                return new Region(new Complex(double.Parse(min[0]), double.Parse(min[1])), 
                    new Complex(double.Parse(max[0]), double.Parse(max[1])), bool.Parse(data[2]));
            }
            public override string ToString() => $"Min: {Min}, Max: {Max}, OriginAndWidth: {OriginAndWidth}";
        }

        public struct Gradient : IEquatable<Gradient>
        {
            public bool Equals(Gradient other)
            {
                return Scale.Equals(other.Scale) && Shift.Equals(other.Shift) && Logindex == other.Logindex;
            }

            public override bool Equals(object obj)
            {
                return (!ReferenceEquals(null, obj)) && obj is Gradient && Equals((Gradient) obj);
            }

            public override int GetHashCode()
            {
                var hashCode = Scale.GetHashCode();
                hashCode = (hashCode*397) ^ Shift.GetHashCode();
                hashCode = (hashCode*397) ^ Logindex.GetHashCode();
                return hashCode;
            }

            public static bool operator ==(Gradient left, Gradient right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(Gradient left, Gradient right)
            {
                return !left.Equals(right);
            }

            public static Gradient FromString(string load)
            {
                var data = load.Split(' '); 
                return new Gradient(double.Parse(data[0]), double.Parse(data[1]), bool.Parse(data[2]));
            }
            public override string ToString() => $"Scale: {Scale}, Logindex: {Logindex}, Shift: {Shift}";

            public Gradient(double scale, double shift, bool logindex = false)
            {
                Scale = scale;
                Shift = shift;
                Logindex = logindex;
            }

            public double Scale { get; }
            public double Shift { get; }
            public bool Logindex { get; }
        }
    }
}