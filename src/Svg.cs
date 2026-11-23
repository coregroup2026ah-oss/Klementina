using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using W = System.Windows;
using WM = System.Windows.Media;
using WMI = System.Windows.Media.Imaging;

namespace Klementina
{
    /// A compact SVG renderer (icons, logos, charts): paths, basic shapes, groups, transforms, fills/strokes,
    /// opacity, linear/radial gradients and simple text. Renders to a bitmap through WPF.
    static class Svg
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class Style
        {
            public string Fill = "black", Stroke = "none", FillRule = "nonzero", FontFamily = "Segoe UI", FontWeight = "normal", TextAnchor = "start";
            public double StrokeWidth = 1, Opacity = 1, FillOpacity = 1, StrokeOpacity = 1, FontSize = 16;
            public Style Clone() { return (Style)MemberwiseClone(); }
        }

        public static WMI.BitmapSource Render(string path)
        {
            var doc = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using (var r = XmlReader.Create(path, settings)) doc.Load(r);
            var root = doc.DocumentElement;
            double vx = 0, vy = 0, vw = 0, vh = 0;
            var vb = Attr(root, "viewBox");
            if (vb != null)
            {
                var n = Numbers(vb);
                if (n.Count == 4) { vx = n[0]; vy = n[1]; vw = n[2]; vh = n[3]; }
            }
            double w = Length(Attr(root, "width"), vw), h = Length(Attr(root, "height"), vh);
            if (w <= 0) w = vw > 0 ? vw : 300;
            if (h <= 0) h = vh > 0 ? vh : 150;
            if (vw <= 0) { vw = w; vh = h; }
            // vectors: render crisp — at least 1024 px on the long side, at most 4096
            double scale = Math.Max(1, 1024 / Math.Max(w, h));
            scale = Math.Min(scale, 4096 / Math.Max(w, h));
            int pw = Math.Max(1, (int)Math.Round(w * scale)), ph = Math.Max(1, (int)Math.Round(h * scale));

            var defs = new Dictionary<string, XmlElement>();
            foreach (XmlElement e in doc.GetElementsByTagName("*")) { var id = Attr(e, "id"); if (id != null) defs[id] = e; }

            var dv = new WM.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new WM.ScaleTransform(pw / vw, ph / vh));
                dc.PushTransform(new WM.TranslateTransform(-vx, -vy));
                DrawChildren(dc, root, new Style(), defs);
                dc.Pop(); dc.Pop();
            }
            var rtb = new WMI.RenderTargetBitmap(pw, ph, 96, 96, WM.PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        static string Attr(XmlElement e, string name)
        {
            var a = e.Attributes[name];
            return a == null ? null : a.Value;
        }

        static List<double> Numbers(string s)
        {
            return Regex.Matches(s ?? "", @"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?").Cast<Match>().Select(m => double.Parse(m.Value, Inv)).ToList();
        }

        static double Length(string s, double relativeTo)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim();
            var n = Numbers(s);
            if (n.Count == 0) return 0;
            if (s.EndsWith("%")) return relativeTo * n[0] / 100;
            if (s.EndsWith("pt")) return n[0] * 4 / 3.0;
            if (s.EndsWith("mm")) return n[0] * 96 / 25.4;
            if (s.EndsWith("cm")) return n[0] * 96 / 2.54;
            if (s.EndsWith("in")) return n[0] * 96;
            return n[0];
        }

        static double Num(XmlElement e, string name, double def = 0)
        {
            var v = Attr(e, name);
            if (v == null) return def;
            var n = Numbers(v);
            return n.Count > 0 ? n[0] : def;
        }

        static Style Inherit(XmlElement e, Style parent)
        {
            var st = parent.Clone();
            st.Opacity = 1;
            var props = new Dictionary<string, string>();
            foreach (var p in new[] { "fill", "stroke", "stroke-width", "opacity", "fill-opacity", "stroke-opacity", "fill-rule", "font-size", "font-family", "font-weight", "text-anchor" })
            {
                var v = Attr(e, p);
                if (v != null) props[p] = v;
            }
            var style = Attr(e, "style");
            if (style != null)
                foreach (var decl in style.Split(';'))
                {
                    int i = decl.IndexOf(':');
                    if (i > 0) props[decl.Substring(0, i).Trim()] = decl.Substring(i + 1).Trim();
                }
            string v2;
            if (props.TryGetValue("fill", out v2)) st.Fill = v2;
            if (props.TryGetValue("stroke", out v2)) st.Stroke = v2;
            if (props.TryGetValue("stroke-width", out v2)) st.StrokeWidth = Length(v2, 1);
            if (props.TryGetValue("opacity", out v2)) st.Opacity = Numbers(v2).DefaultIfEmpty(1).First();
            if (props.TryGetValue("fill-opacity", out v2)) st.FillOpacity = Numbers(v2).DefaultIfEmpty(1).First();
            if (props.TryGetValue("stroke-opacity", out v2)) st.StrokeOpacity = Numbers(v2).DefaultIfEmpty(1).First();
            if (props.TryGetValue("fill-rule", out v2)) st.FillRule = v2;
            if (props.TryGetValue("font-size", out v2)) st.FontSize = Length(v2, 16);
            if (props.TryGetValue("font-family", out v2)) st.FontFamily = v2.Split(',')[0].Trim('\'', '"', ' ');
            if (props.TryGetValue("font-weight", out v2)) st.FontWeight = v2;
            if (props.TryGetValue("text-anchor", out v2)) st.TextAnchor = v2;
            return st;
        }

        static WM.Transform ParseTransform(string s)
        {
            var group = new WM.TransformGroup();
            foreach (Match m in Regex.Matches(s ?? "", @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
            {
                var n = Numbers(m.Groups[2].Value);
                WM.Transform t = null;
                switch (m.Groups[1].Value)
                {
                    case "matrix": if (n.Count == 6) t = new WM.MatrixTransform(n[0], n[1], n[2], n[3], n[4], n[5]); break;
                    case "translate": t = new WM.TranslateTransform(n.Count > 0 ? n[0] : 0, n.Count > 1 ? n[1] : 0); break;
                    case "scale": t = new WM.ScaleTransform(n.Count > 0 ? n[0] : 1, n.Count > 1 ? n[1] : (n.Count > 0 ? n[0] : 1)); break;
                    case "rotate": t = n.Count >= 3 ? new WM.RotateTransform(n[0], n[1], n[2]) : new WM.RotateTransform(n.Count > 0 ? n[0] : 0); break;
                    case "skewX": t = new WM.SkewTransform(n.Count > 0 ? n[0] : 0, 0); break;
                    case "skewY": t = new WM.SkewTransform(0, n.Count > 0 ? n[0] : 0); break;
                }
                if (t != null) group.Children.Insert(0, t); // SVG applies the rightmost transform first
            }
            return group;
        }

        static WM.Color? ParseColor(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "none" || s == "transparent") return null;
            if (s == "currentColor") return WM.Colors.Black;
            var rgb = Regex.Match(s, @"rgba?\(([^)]*)\)");
            if (rgb.Success)
            {
                var parts = rgb.Groups[1].Value.Split(',').Select(p => p.Trim()).ToList();
                Func<string, byte> ch = p => (byte)Math.Max(0, Math.Min(255, p.EndsWith("%") ? Numbers(p)[0] * 2.55 : Numbers(p)[0]));
                byte a = parts.Count > 3 ? (byte)(Numbers(parts[3])[0] * 255) : (byte)255;
                return WM.Color.FromArgb(a, ch(parts[0]), ch(parts[1]), ch(parts[2]));
            }
            if (Regex.IsMatch(s, "^#[0-9a-fA-F]{3}$")) s = "#" + s[1] + s[1] + s[2] + s[2] + s[3] + s[3];
            try { return (WM.Color)WM.ColorConverter.ConvertFromString(s); } catch { return null; }
        }

        static WM.Brush MakeBrush(string paint, double opacity, Dictionary<string, XmlElement> defs)
        {
            if (paint == null) return null;
            var url = Regex.Match(paint, @"url\(\s*#([^)\s]+)\s*\)");
            if (url.Success)
            {
                XmlElement g;
                if (!defs.TryGetValue(url.Groups[1].Value, out g)) return null;
                var stops = new WM.GradientStopCollection();
                var stopSource = g;
                var href = Attr(g, "xlink:href") ?? Attr(g, "href");
                if (g.GetElementsByTagName("stop").Count == 0 && href != null && defs.ContainsKey(href.TrimStart('#'))) stopSource = defs[href.TrimStart('#')];
                foreach (XmlElement stop in stopSource.GetElementsByTagName("stop"))
                {
                    var st = Inherit(stop, new Style());
                    string col = Attr(stop, "stop-color"), op = Attr(stop, "stop-opacity");
                    var styleAttr = Attr(stop, "style");
                    if (styleAttr != null)
                    {
                        var mc = Regex.Match(styleAttr, @"stop-color\s*:\s*([^;]+)"); if (mc.Success) col = mc.Groups[1].Value.Trim();
                        var mo = Regex.Match(styleAttr, @"stop-opacity\s*:\s*([^;]+)"); if (mo.Success) op = mo.Groups[1].Value.Trim();
                    }
                    var c = ParseColor(col ?? "black") ?? WM.Colors.Black;
                    double o = (op != null ? Numbers(op).DefaultIfEmpty(1).First() : 1) * opacity;
                    c.A = (byte)(c.A * o);
                    string off = Attr(stop, "offset") ?? "0";
                    double offset = off.EndsWith("%") ? Numbers(off)[0] / 100 : Numbers(off).DefaultIfEmpty(0).First();
                    stops.Add(new WM.GradientStop(c, offset));
                }
                bool user = Attr(g, "gradientUnits") == "userSpaceOnUse";
                WM.GradientBrush b;
                if (g.LocalName == "radialGradient")
                {
                    var rb = new WM.RadialGradientBrush(stops);
                    double cx = Length(Attr(g, "cx") ?? "50%", 1), cy = Length(Attr(g, "cy") ?? "50%", 1), r = Length(Attr(g, "r") ?? "50%", 1);
                    rb.Center = rb.GradientOrigin = new W.Point(cx, cy);
                    rb.RadiusX = rb.RadiusY = r;
                    b = rb;
                }
                else
                {
                    var lb = new WM.LinearGradientBrush(stops);
                    lb.StartPoint = new W.Point(Length(Attr(g, "x1") ?? "0%", 1), Length(Attr(g, "y1") ?? "0%", 1));
                    lb.EndPoint = new W.Point(Length(Attr(g, "x2") ?? "100%", 1), Length(Attr(g, "y2") ?? "0%", 1));
                    b = lb;
                }
                b.MappingMode = user ? WM.BrushMappingMode.Absolute : WM.BrushMappingMode.RelativeToBoundingBox;
                var gt = Attr(g, "gradientTransform");
                if (gt != null) b.Transform = ParseTransform(gt);
                return b;
            }
            var col2 = ParseColor(paint);
            if (col2 == null) return null;
            var cc = col2.Value;
            cc.A = (byte)(cc.A * Math.Max(0, Math.Min(1, opacity)));
            return new WM.SolidColorBrush(cc);
        }

        static void DrawChildren(WM.DrawingContext dc, XmlElement parent, Style style, Dictionary<string, XmlElement> defs)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                var e = node as XmlElement;
                if (e == null) continue;
                string name = e.LocalName;
                if (name == "defs" || name == "linearGradient" || name == "radialGradient" || name == "clipPath" || name == "mask" ||
                    name == "metadata" || name == "title" || name == "desc" || name == "style" || name == "symbol" || name == "pattern" || name == "filter") continue;
                if (Attr(e, "display") == "none" || Attr(e, "visibility") == "hidden") continue;
                var st = Inherit(e, style);
                var tr = Attr(e, "transform");
                int pushes = 0;
                if (tr != null) { dc.PushTransform(ParseTransform(tr)); pushes++; }
                if (st.Opacity < 1) { dc.PushOpacity(st.Opacity); pushes++; }
                try
                {
                    if (name == "g" || name == "svg" || name == "a") DrawChildren(dc, e, st, defs);
                    else if (name == "use")
                    {
                        var href = (Attr(e, "xlink:href") ?? Attr(e, "href") ?? "").TrimStart('#');
                        XmlElement target;
                        if (defs.TryGetValue(href, out target))
                        {
                            dc.PushTransform(new WM.TranslateTransform(Num(e, "x"), Num(e, "y")));
                            var wrapper = (XmlElement)target.OwnerDocument.CreateElement("g", target.NamespaceURI);
                            wrapper.AppendChild(target.CloneNode(true));
                            DrawChildren(dc, wrapper, st, defs);
                            dc.Pop();
                        }
                    }
                    else if (name == "text") DrawText(dc, e, st, defs);
                    else
                    {
                        var geo = Geometry(e);
                        if (geo != null) Paint(dc, geo, st, defs);
                    }
                }
                catch { /* skip shapes we can't parse rather than failing the whole image */ }
                for (int i = 0; i < pushes; i++) dc.Pop();
            }
        }

        static WM.Geometry Geometry(XmlElement e)
        {
            switch (e.LocalName)
            {
                case "path":
                    var d = Attr(e, "d");
                    if (string.IsNullOrWhiteSpace(d)) return null;
                    return WM.Geometry.Parse(NormalizePath(d));
                case "rect":
                    double w = Num(e, "width"), h = Num(e, "height");
                    if (w <= 0 || h <= 0) return null;
                    double rx = Num(e, "rx", -1), ry = Num(e, "ry", -1);
                    if (rx < 0) rx = ry < 0 ? 0 : ry;
                    if (ry < 0) ry = rx;
                    return new WM.RectangleGeometry(new W.Rect(Num(e, "x"), Num(e, "y"), w, h), Math.Min(rx, w / 2), Math.Min(ry, h / 2));
                case "circle":
                    return new WM.EllipseGeometry(new W.Point(Num(e, "cx"), Num(e, "cy")), Num(e, "r"), Num(e, "r"));
                case "ellipse":
                    return new WM.EllipseGeometry(new W.Point(Num(e, "cx"), Num(e, "cy")), Num(e, "rx"), Num(e, "ry"));
                case "line":
                    return new WM.LineGeometry(new W.Point(Num(e, "x1"), Num(e, "y1")), new W.Point(Num(e, "x2"), Num(e, "y2")));
                case "polyline":
                case "polygon":
                    var n = Numbers(Attr(e, "points"));
                    if (n.Count < 4) return null;
                    var fig = new WM.PathFigure { StartPoint = new W.Point(n[0], n[1]), IsClosed = e.LocalName == "polygon" };
                    for (int i = 2; i + 1 < n.Count; i += 2) fig.Segments.Add(new WM.LineSegment(new W.Point(n[i], n[i + 1]), true));
                    var pg = new WM.PathGeometry();
                    pg.Figures.Add(fig);
                    return pg;
            }
            return null;
        }

        /// WPF's path syntax is close to SVG's; spell out separators it is stricter about.
        static string NormalizePath(string d)
        {
            d = Regex.Replace(d, @"([a-zA-Z])", " $1 ");
            d = Regex.Replace(d, @"(\d)-", "$1 -");
            d = Regex.Replace(d, @"(\.\d+)(?=\.)", "$1 ");   // "1.5.5" → "1.5 .5"
            // arc flags written without separators, e.g. "a1 1 0 011 1"
            d = Regex.Replace(d, @"([aA])\s*([^a-zA-Z]+)", m => m.Groups[1].Value + " " + FixArcs(m.Groups[2].Value));
            return d.Replace(',', ' ').Trim();
        }

        static string FixArcs(string args)
        {
            // each arc takes 7 values: rx ry rotation large-arc-flag sweep-flag x y; the two flags may be glued
            // to what follows ("011 1" = 0, 1, 1 1)
            var queue = Regex.Matches(args, @"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?").Cast<Match>().Select(m => m.Value).ToList();
            var tokens = new List<string>();
            int k = 0;
            while (queue.Count > 0)
            {
                string t = queue[0];
                queue.RemoveAt(0);
                if ((k == 3 || k == 4) && t.Length > 1 && (t[0] == '0' || t[0] == '1') && t[1] != '.')
                {
                    tokens.Add(t[0].ToString());
                    queue.Insert(0, t.Substring(1));
                }
                else tokens.Add(t);
                k = (k + 1) % 7;
            }
            return string.Join(" ", tokens) + " ";
        }

        static void Paint(WM.DrawingContext dc, WM.Geometry geo, Style st, Dictionary<string, XmlElement> defs)
        {
            var pg = geo as WM.PathGeometry;
            if (pg == null && st.FillRule == "evenodd") pg = WM.PathGeometry.CreateFromGeometry(geo);
            if (pg != null) pg.FillRule = st.FillRule == "evenodd" ? WM.FillRule.EvenOdd : WM.FillRule.Nonzero;
            var fill = MakeBrush(st.Fill, st.FillOpacity, defs);
            var strokeBrush = MakeBrush(st.Stroke, st.StrokeOpacity, defs);
            WM.Pen pen = strokeBrush != null && st.StrokeWidth > 0 ? new WM.Pen(strokeBrush, st.StrokeWidth) { LineJoin = WM.PenLineJoin.Round, StartLineCap = WM.PenLineCap.Round, EndLineCap = WM.PenLineCap.Round } : null;
            if (fill != null || pen != null) dc.DrawGeometry(fill, pen, (WM.Geometry)pg ?? geo);
        }

        static void DrawText(WM.DrawingContext dc, XmlElement e, Style st, Dictionary<string, XmlElement> defs)
        {
            string text = e.InnerText.Trim();
            if (text.Length == 0) return;
            var brush = MakeBrush(st.Fill, st.FillOpacity, defs) ?? WM.Brushes.Black;
            var weight = st.FontWeight == "bold" || Numbers(st.FontWeight).DefaultIfEmpty(400).First() >= 600 ? W.FontWeights.Bold : W.FontWeights.Normal;
            var ft = new WM.FormattedText(text, CultureInfo.CurrentCulture,
                text.Any(c => c >= '؀' && c <= 'ۿ') ? W.FlowDirection.RightToLeft : W.FlowDirection.LeftToRight,
                new WM.Typeface(new WM.FontFamily(st.FontFamily), W.FontStyles.Normal, weight, W.FontStretches.Normal), Math.Max(1, st.FontSize), brush, 1.0);
            double x = Num(e, "x"), y = Num(e, "y") - ft.Baseline;
            if (ft.FlowDirection == W.FlowDirection.RightToLeft) x += ft.Width; // RTL text hangs left of its origin
            if (st.TextAnchor == "middle") x -= ft.Width / 2;
            else if (st.TextAnchor == "end") x -= ft.Width;
            dc.DrawText(ft, new W.Point(x, y));
        }
    }
}
