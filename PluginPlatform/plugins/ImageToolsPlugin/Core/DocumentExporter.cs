using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using W = DocumentFormat.OpenXml.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using XDR = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ImageToolsPlugin.Core;

/// <summary>生成标准 PDF、DOCX 和 XLSX 文件；不依赖本机安装 Office。</summary>
internal static class DocumentExporter
{
    /// <summary>把多张图片按顺序写入一个 PDF，每页一图，保持比例并自动选择横竖页面。</summary>
    internal static void Pdf(string target, IReadOnlyList<ImageAsset> assets, CancellationToken token)
    {
        using var file = File.Create(target);
        using var document = SKDocument.CreatePdf(file) ?? throw new IOException("当前环境不能创建 PDF。");
        foreach (var asset in assets)
        {
            token.ThrowIfCancellationRequested();
            using var bitmap = ImageProcessor.Decode(asset.WorkingPath);
            var width = bitmap.Width > bitmap.Height ? 842f : 595f;
            var height = bitmap.Width > bitmap.Height ? 595f : 842f;
            var scale = Math.Min((width - 48) / bitmap.Width, (height - 48) / bitmap.Height);
            var canvas = document.BeginPage(width, height);
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            var x = (width - bitmap.Width * scale) / 2; var y = (height - bitmap.Height * scale) / 2;
            canvas.DrawImage(image, new SKRect(x, y, x + bitmap.Width * scale, y + bitmap.Height * scale));
            document.EndPage();
        }
        document.Close();
    }

    /// <summary>把多张图片导出为 Word；识别模式输出可编辑段落，图片模式保留图片外观。</summary>
    internal static void Word(string target, IReadOnlyList<ImageAsset> assets, bool recognize,
        Func<ImageAsset, RecognizedLine[]> getText, CancellationToken token)
    {
        using var document = WordprocessingDocument.Create(target, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var body = new W.Body();
        main.Document = new W.Document(body);
        for (var index = 0; index < assets.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var asset = assets[index];
            if (index > 0) body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));
            body.Append(new W.Paragraph(new W.Run(new W.RunProperties(new W.Bold()), new W.Text(asset.Name))));
            if (recognize)
            {
                var lines = getText(asset);
                if (lines.Length == 0) body.Append(new W.Paragraph(new W.Run(new W.Text("未识别到文字"))));
                foreach (var row in OcrService.GroupRows(lines))
                    body.Append(new W.Paragraph(new W.Run(new W.Text(CleanText(string.Join("  ", row.Select(line => line.Text)))) { Space = SpaceProcessingModeValues.Preserve })));
            }
            else
            {
                using var bitmap = ImageProcessor.Decode(asset.WorkingPath);
                var part = main.AddImagePart(ImagePartType.Png);
                using (var input = new MemoryStream(ImageProcessor.Encode(bitmap, "png", 100))) part.FeedData(input);
                var scale = Math.Min(6.0 / bitmap.Width, 8.5 / bitmap.Height);
                var cx = (long)(bitmap.Width * scale * 914400); var cy = (long)(bitmap.Height * scale * 914400);
                var picture = new PIC.Picture(
                    new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = (uint)index + 1, Name = "图片" },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = main.GetIdOfPart(part) }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
                body.Append(new W.Paragraph(new W.Run(new W.Drawing(
                    new DW.Inline(new DW.Extent { Cx = cx, Cy = cy }, new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                        new DW.DocProperties { Id = (uint)index + 1, Name = "图片" },
                        new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                        new A.Graphic(new A.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))))));
            }
        }
        body.Append(new W.SectionProperties(new W.PageSize { Width = 11906, Height = 16838 },
            new W.PageMargin { Top = 720, Bottom = 720, Left = 720, Right = 720 }));
        main.Document.Save();
    }

    /// <summary>每张图片生成一个 Excel 工作表；识别模式按文字位置组织行列，所有内容以文本写入。</summary>
    internal static void Excel(string target, IReadOnlyList<ImageAsset> assets, bool recognize,
        Func<ImageAsset, RecognizedLine[]> getText, CancellationToken token)
    {
        using var document = SpreadsheetDocument.Create(target, SpreadsheetDocumentType.Workbook);
        var book = document.AddWorkbookPart();
        var sheets = new S.Sheets(); book.Workbook = new S.Workbook(sheets);
        for (var index = 0; index < assets.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var asset = assets[index];
            var part = book.AddNewPart<WorksheetPart>();
            var data = new S.SheetData();
            part.Worksheet = new S.Worksheet(new S.Columns(new S.Column { Min = 1, Max = 256, Width = 24, CustomWidth = true }), data);
            sheets.Append(new S.Sheet { Id = book.GetIdOfPart(part), SheetId = (uint)index + 1, Name = $"图片{index + 1}" });
            if (recognize)
            {
                var rows = OcrService.GroupRows(getText(asset));
                // 用全页的列起点对齐，某一行缺少单元格时仍保留空列。
                var anchors = new List<float>();
                foreach (var line in rows.SelectMany(row => row).OrderBy(line => line.X))
                {
                    if (anchors.Count == 0 || line.X - anchors[^1] > Math.Max(12, line.Height))
                        anchors.Add(line.X);
                }
                if (anchors.Count > 256) throw new IOException("识别到的列数过多，请裁剪表格区域后重新识别。");
                for (var r = 0; r < rows.Length; r++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = new S.Row { RowIndex = (uint)r + 1 };
                    var cells = new SortedDictionary<int, string>();
                    foreach (var line in rows[r])
                    {
                        var c = Enumerable.Range(0, anchors.Count).MinBy(c => Math.Abs(anchors[c] - line.X));
                        cells[c] = cells.TryGetValue(c, out var current) ? current + " " + line.Text : line.Text;
                    }
                    foreach (var cell in cells)
                        row.Append(TextCell(ColumnName(cell.Key) + (r + 1), cell.Value));
                    data.Append(row);
                }
                if (rows.Length == 0) data.Append(new S.Row(TextCell("A1", "未识别到文字")) { RowIndex = 1 });
            }
            else
            {
                using var bitmap = ImageProcessor.Decode(asset.WorkingPath);
                var drawing = part.AddNewPart<DrawingsPart>();
                var imagePart = drawing.AddImagePart(ImagePartType.Png);
                using (var input = new MemoryStream(ImageProcessor.Encode(bitmap, "png", 100))) imagePart.FeedData(input);
                var scale = Math.Min(1d, 1000d / Math.Max(bitmap.Width, bitmap.Height));
                var cx = (long)(bitmap.Width * scale * 9525); var cy = (long)(bitmap.Height * scale * 9525);
                drawing.WorksheetDrawing = new XDR.WorksheetDrawing(
                    new XDR.OneCellAnchor(new XDR.FromMarker(new XDR.ColumnId("0"), new XDR.ColumnOffset("0"), new XDR.RowId("0"), new XDR.RowOffset("0")),
                        new XDR.Extent { Cx = cx, Cy = cy },
                        new XDR.Picture(new XDR.NonVisualPictureProperties(new XDR.NonVisualDrawingProperties { Id = 1, Name = "图片" },
                                new XDR.NonVisualPictureDrawingProperties()),
                            new XDR.BlipFill(new A.Blip { Embed = drawing.GetIdOfPart(imagePart) }, new A.Stretch(new A.FillRectangle())),
                            new XDR.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })),
                        new XDR.ClientData()));
                drawing.WorksheetDrawing.Save();
                part.Worksheet.Append(new S.Drawing { Id = part.GetIdOfPart(drawing) });
            }
            part.Worksheet.Save();
        }
        book.Workbook.Save();
    }

    /// <summary>构造内联文本单元格；以等号开头的识别内容也不会成为公式。</summary>
    private static S.Cell TextCell(string reference, string text)
    {
        var value = CleanText(text);
        if (value.Length > 32767) value = value[..32767];
        return new S.Cell(new S.InlineString(new S.Text(value) { Space = SpaceProcessingModeValues.Preserve }))
            { CellReference = reference, DataType = S.CellValues.InlineString };
    }

    /// <summary>移除 XML 不允许的控制字符，保留换行和制表符。</summary>
    private static string CleanText(string value)
    {
        return new string(value.Where(c => c >= 32 || c is '\n' or '\r' or '\t').ToArray());
    }

    /// <summary>将从零开始的列编号转换为 A、B、AA 等 Excel 列名。</summary>
    private static string ColumnName(int index)
    {
        var name = ""; index++;
        while (index > 0) { index--; name = (char)('A' + index % 26) + name; index /= 26; }
        return name;
    }
}
