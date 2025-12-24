using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic.Reporting;

/// <summary>
/// Generates a PDF report tailored for LLM Vision analysis or Archiving.
/// Uses QuestPDF to handle complex layouting.
/// </summary>
public class PdfReportGenerator {
    private readonly string _projectName;
    private readonly string _structureContent;
    private readonly Dictionary<string, string> _processedFilesMap;
    private readonly HashSet<string> _filesSelectedForMerge;
    private readonly PdfMode _mode;

    public PdfReportGenerator(
        string projectName,
        string structureContent,
        Dictionary<string, string> processedFilesMap,
        HashSet<string> filesSelectedForMerge,
        PdfMode mode) {
        
        _projectName = projectName;
        _structureContent = structureContent;
        _processedFilesMap = processedFilesMap;
        _filesSelectedForMerge = filesSelectedForMerge;
        _mode = mode;
    }

    public void Generate(string outputFilePath) {
        // Config based on mode
        float margin;
        float fontSize;
        float lineHeight;

        switch (_mode) {
            case PdfMode.Compact:
                margin = 0.8f;
                fontSize = 8f;
                lineHeight = 1.0f;
                break;
            case PdfMode.Extreme:
                // Экстремальная экономия:
                // Шрифт 2pt, поля 0.3см, интерлиньяж 0.9 (чуть свободнее чем 0.8 во избежание наслоений строк)
                margin = 0.05f; 
                fontSize = 1f; 
                lineHeight = 0.8f; 
                break;
            case PdfMode.Standard:
            default:
                margin = 1.5f;
                fontSize = 11f;
                lineHeight = 1.2f;
                break;
        }

        Document.Create(container => {
            container.Page(page => {
                // Settings
                page.Size(PageSizes.A4);
                page.Margin(margin, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(fontSize).FontFamily(Fonts.CourierNew).LineHeight(lineHeight));

                // Header (Page Title)
                // In Extreme mode, we skip page header entirely
                if (_mode != PdfMode.Extreme) {
                    page.Header()
                        .PaddingBottom(5)
                        .Text(text => {
                            text.Span($"{_projectName} - Code Report").SemiBold().FontSize(fontSize - 1).FontColor(Colors.Grey.Medium);
                            if (_mode == PdfMode.Compact) text.Span(" [Compact]").FontColor(Colors.Grey.Lighten1);
                            text.AlignRight();
                        });
                }

                // Footer (Page Numbers)
                // In Extreme mode, skip footer
                if (_mode != PdfMode.Extreme) {
                    page.Footer()
                        .AlignCenter()
                        .Text(x => {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                        });
                }

                // Content
                page.Content()
                    .Column(column => {
                        // 1. Structure
                        // В режиме Extreme убираем заголовок "Project Structure" полностью, чтобы не наслаивался
                        if (_mode != PdfMode.Extreme) {
                            column.Item().Text(t => {
                                t.Span("Project Structure").FontSize(fontSize + 4).Bold();
                                t.EmptyLine();
                            });
                        }

                        // Structure content
                        float structPadding = _mode == PdfMode.Extreme ? 0 : 5;
                        column.Item().Background(Colors.Grey.Lighten4).Padding(structPadding).Text(_structureContent).FontSize(fontSize);
                        
                        // Separator
                        if (_mode == PdfMode.Standard) {
                            column.Item().PageBreak();
                        }
                        else {
                            // In Extreme/Compact: simple separator
                            float bottomPad = _mode == PdfMode.Extreme ? 2 : 10;
                            column.Item().PaddingBottom(bottomPad).LineHorizontal(0.5f).LineColor(Colors.Black);
                        }

                        // 2. Files
                        var sortedFiles = _processedFilesMap.OrderBy(kvp => kvp.Key);

                        foreach (var entry in sortedFiles) {
                            string originalPath = entry.Key;
                            string processedPath = entry.Value;
                            string fileName = Path.GetFileName(originalPath);
                            bool isStub = !_filesSelectedForMerge.Contains(originalPath);

                            string content;
                            try {
                                content = File.ReadAllText(processedPath);
                            } catch { content = "[Error reading file]"; }

                            if (isStub) {
                                content = "[STUB]";
                            }

                            if (_mode == PdfMode.Extreme) {
                                // === EXTREME MODE RENDERER ===
                                // Strict REGULAR font (no bold), text-based headers
                                column.Item().PaddingTop(2).Column(c => {
                                    c.Item().Text(t => {
                                        // Plain text header, no bold
                                        string headerText = $">>> {fileName}";
                                        if (isStub) headerText += " (Stub)";
                                        t.Span(headerText); 
                                    });
                                    c.Item().Text(content);
                                });
                            }
                            else if (_mode == PdfMode.Compact) {
                                // === COMPACT MODE RENDERER ===
                                column.Item().PaddingTop(5).Column(c => {
                                    c.Item().Background(Colors.Grey.Lighten3).BorderBottom(1).BorderColor(Colors.Black).Padding(2).Row(row => {
                                        row.RelativeItem().Text(t => {
                                            t.Span(fileName).Bold();
                                            if (isStub) t.Span(" (Stub)").Italic();
                                        });
                                    });
                                    c.Item().Text(content);
                                });
                            }
                            else {
                                // === STANDARD MODE RENDERER ===
                                int lineCount = content.Count(c => c == '\n') + 1;
                                bool isSmallFile = lineCount < 20;

                                column.Item().PaddingTop(15).Element(block => {
                                    if (isSmallFile) {
                                        block.ShowEntire().Column(c => RenderStandardBlock(c, fileName, content, isStub));
                                    }
                                    else {
                                        block.Column(c => RenderStandardBlock(c, fileName, content, isStub));
                                    }
                                });
                            }
                        }
                    });
            });
        }).GeneratePdf(outputFilePath);
    }

    private void RenderStandardBlock(ColumnDescriptor column, string fileName, string content, bool isStub) {
        // Visual Header with blue background
        column.Item().Background(Colors.Blue.Lighten5).BorderBottom(1).BorderColor(Colors.Blue.Medium).Padding(5).Row(row => {
            row.RelativeItem().Text(t => {
                t.Span("FILE: ").Bold();
                t.Span(fileName).SemiBold();
                if (isStub) t.Span(" (Stub)").FontColor(Colors.Grey.Darken2).Italic();
            });
        });

        // Content
        column.Item().PaddingTop(5).PaddingBottom(10).Text(content);
    }
}