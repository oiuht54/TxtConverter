using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TxtConverter.Core.Enums;
using TxtConverter.Services;

namespace TxtConverter.Core.Logic.Reporting;

/// <summary>
/// Генератор PDF.
/// Оптимизирован для сверхкомпактного представления без избыточных пустот.
/// Поддерживает неразрывные элементы структуры.
/// </summary>
public class PdfReportGenerator {
    private readonly string _sourceDirPath;
    private readonly string _projectName;
    private readonly string _structureContent;
    private readonly Dictionary<string, string> _processedFilesMap;
    private readonly HashSet<string> _filesSelectedForMerge;
    private readonly PdfMode _mode;

    public PdfReportGenerator(
        string sourceDirPath,
        string structureContent,
        Dictionary<string, string> processedFilesMap,
        HashSet<string> filesSelectedForMerge,
        PdfMode mode) {
        _sourceDirPath = sourceDirPath;
        _projectName = Path.GetFileName(sourceDirPath);
        _structureContent = structureContent;
        _processedFilesMap = processedFilesMap;
        _filesSelectedForMerge = filesSelectedForMerge;
        _mode = mode;
    }

    public void Generate(string outputFilePath) {
        // Настройки плотности документа
        float margin;
        float fontSize;
        float lineHeight;

        switch (_mode) {
            case PdfMode.Compact:
                margin = 0.6f;
                fontSize = 8f;
                lineHeight = 1.0f;
                break;
            case PdfMode.Extreme:
                margin = 0.05f;
                fontSize = 1f;
                lineHeight = 0.8f;
                break;
            case PdfMode.Standard:
            default:
                margin = 1.0f;     // Уменьшено с 1.5 для расширения рабочей области
                fontSize = 9.5f;   // Уменьшено с 11.0 для компактности кода
                lineHeight = 1.1f; // Уменьшено с 1.2
                break;
        }

        string stubLabel = LanguageManager.Instance.GetString("report_stub_label");
        string stubMessage = LanguageManager.Instance.GetString("report_omitted");
        string additionalInfoWarning = LanguageManager.Instance.GetString("report_stub_warning");

        Document.Create(container => {
            container.Page(page => {
                page.Size(PageSizes.A4);
                page.Margin(margin, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(fontSize).FontFamily(Fonts.CourierNew).LineHeight(lineHeight));

                // Шапка документа
                if (_mode != PdfMode.Extreme) {
                    page.Header()
                        .PaddingBottom(3)
                        .Text(text => {
                            text.Span($"{_projectName} - Code Report").SemiBold().FontSize(fontSize - 1).FontColor(Colors.Grey.Medium);
                            if (_mode == PdfMode.Compact) text.Span(" [Compact]").FontColor(Colors.Grey.Lighten1);
                            text.AlignRight();
                        });
                }

                // Номера страниц
                if (_mode != PdfMode.Extreme) {
                    page.Footer()
                        .PaddingTop(3)
                        .AlignCenter()
                        .Text(x => {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                        });
                }

                // Основное содержимое
                page.Content()
                    .Column(column => {
                        // 1. АКЦЕНТНЫЙ БЛОК ПРЕДУПРЕЖДЕНИЯ (Компактный)
                        if (_mode != PdfMode.Extreme) {
                            column.Item()
                                .Background(Colors.Amber.Lighten5)
                                .BorderLeft(3)
                                .BorderColor(Colors.Amber.Medium)
                                .Padding(8)
                                .Text(additionalInfoWarning)
                                .FontSize(fontSize)
                                .FontColor(Colors.Grey.Darken4);
                            
                            column.Item().Height(8); // Минимальный разделительный отступ
                        } else {
                            column.Item().Text(additionalInfoWarning).FontSize(fontSize);
                            column.Item().Height(3);
                        }

                        // 2. БЛОК СТРУКТУРЫ (Растянут по ширине страницы, очищен от скрытых \n)
                        if (!string.IsNullOrWhiteSpace(_structureContent)) {
                            float structPadding = _mode == PdfMode.Extreme ? 0 : 6;
                            
                            column.Item()
                                .Background(Colors.Grey.Lighten5) // Мягкий фон без жесткой внешней обводки
                                .Padding(structPadding)
                                .Text(_structureContent.Trim())   // Метод Trim() срезает скрытые пустые строки в конце!
                                .FontSize(fontSize);

                            // Сверхплотный отступ перед началом файлов (без громоздких разделительных линий)
                            column.Item().Height(_mode == PdfMode.Extreme ? 2 : 5);
                        }

                        // 3. СПИСОК ФАЙЛОВ С ИХ КОДОМ
                        var sortedFiles = _processedFilesMap
                            .OrderBy(kvp => Path.GetRelativePath(_sourceDirPath, kvp.Key).Replace("\\", "/"), StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        int index = 0;
                        foreach (var entry in sortedFiles) {
                            index++;
                            string originalPath = entry.Key;
                            string processedPath = entry.Value;
                            string relPath = Path.GetRelativePath(_sourceDirPath, originalPath).Replace("\\", "/");
                            bool isStub = !_filesSelectedForMerge.Contains(originalPath);

                            string content;
                            if (isStub) {
                                content = stubMessage;
                            }
                            else {
                                try {
                                    content = File.ReadAllText(processedPath);
                                }
                                catch {
                                    content = "[Error reading file]";
                                }
                            }

                            if (_mode == PdfMode.Extreme) {
                                // === EXTREME ===
                                column.Item().PaddingTop(1).Column(c => {
                                    c.Item().Text(t => {
                                        string headerText = $"{index}. >>> {relPath}";
                                        if (isStub) headerText += stubLabel;
                                        t.Span(headerText);
                                    });
                                    c.Item().Text(content);
                                });
                            }
                            else if (_mode == PdfMode.Compact) {
                                // === COMPACT ===
                                column.Item().PaddingTop(2).Column(c => {
                                    c.Item()
                                        .Background(Colors.Grey.Lighten3)
                                        .BorderBottom(0.5f)
                                        .BorderColor(Colors.Black)
                                        .Padding(2)
                                        .Row(row => {
                                            row.RelativeItem().Text(t => {
                                                t.Span($"{index}. {relPath}").Bold();
                                                if (isStub) t.Span(stubLabel).Italic();
                                            });
                                        });
                                    c.Item().Text(content);
                                });
                            }
                            else {
                                // === STANDARD ===
                                int lineCount = content.Count(ch => ch == '\n') + 1;
                                bool isSmallFile = lineCount < 20;

                                column.Item().PaddingTop(6).Element(block => {
                                    if (isSmallFile) {
                                        block.ShowEntire().Column(c => RenderStandardBlock(c, index, relPath, content, isStub, stubLabel));
                                    }
                                    else {
                                        block.Column(c => RenderStandardBlock(c, index, relPath, content, isStub, stubLabel));
                                    }
                                });
                            }
                        }
                    });
            });
        }).GeneratePdf(outputFilePath);
    }

    private void RenderStandardBlock(ColumnDescriptor column, int index, string relPath, string content, bool isStub, string stubLabel) {
        column.Item()
            .Background(Colors.Blue.Lighten5)
            .BorderBottom(0.5f)
            .BorderColor(Colors.Blue.Medium)
            .Padding(4)
            .Row(row => {
                row.RelativeItem().Text(t => {
                    t.Span($"{index}. FILE: ").Bold();
                    t.Span(relPath).SemiBold();
                    if (isStub) t.Span(stubLabel).FontColor(Colors.Grey.Darken2).Italic();
                });
            });

        column.Item().PaddingTop(3).PaddingBottom(6).Text(content);
    }
}