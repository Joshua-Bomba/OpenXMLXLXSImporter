﻿using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using OpenXMLXLSXImporter.CellData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenXMLXLSXImporter.FileAccess
{
    public class XlsxSheetFile : IXlsxSheetFile, IXlsxSheetFilePromise
    {
        private IXlsxDocumentFile _fileAccess;

        private string _sheetName;

        private Sheet _sheet;
        private WorksheetPart _workbookPart;
        private Worksheet _worksheet;
        private SheetData _sheetData;

        private Task _loadSpreadSheetData;

        private IEnumerable<Row> _rowsEnumerable;
        private IEnumerator<Row> _rowEnumerator;
        private IEnumerable<Cell> _cellEnumerable;
        private Row _row;
        private bool _rowsLoadedIn ;

        private UInt32Value _rowIndexNullable;
        private uint _rowIndex;

        private IDictionary<uint, IEnumerator<Cell>> _rows;

        public XlsxSheetFile(IXlsxDocumentFile fileAccess, string sheetName)
        {
            _fileAccess = fileAccess;
            _sheetName = sheetName;
            _rowsLoadedIn = false;
            _loadSpreadSheetData = Task.Run(LoadSpreadSheetData);
        }

        protected async Task LoadSpreadSheetData()
        {
            _rows = new Dictionary<uint, IEnumerator<Cell>>();
            _sheet = await _fileAccess.GetSheet(_sheetName);
            _workbookPart = await _fileAccess.GetWorkSheetPartById(_sheet.Id);
            _worksheet = _workbookPart.Worksheet;
            _sheetData = _worksheet.Elements<SheetData>().First();
           _rowsEnumerable = _sheetData.Elements<Row>();
            _rowEnumerator = _rowsEnumerable.GetEnumerator();
        }

        public async Task<IXlsxSheetFile> GetLoadedFile()
        {
            await _loadSpreadSheetData;
            return this;
        }

        bool IXlsxSheetFile.TryGetRow(uint desiredRowIndex, out IEnumerator<Cell> cellEnumerator)
        {
            //this will only load in up to the row we need
            //if we try loading in a row that does not exist then we will load all of them in
            //I'm assuming that data from here might still be in the file and we don't want to read things we don't need
            if(_rows.ContainsKey(desiredRowIndex))
            {
                cellEnumerator = _rows[desiredRowIndex];
                return true;
            }
            while (!_rowsLoadedIn)
            {
                if (_rowEnumerator.MoveNext())
                {
                    _row = _rowEnumerator.Current;
                    _rowIndexNullable = _row.RowIndex;
                    if (_rowIndexNullable.HasValue)
                    {
                        _rowIndex = _rowIndexNullable.Value;
                        _cellEnumerable = _row.Elements<Cell>();
                        cellEnumerator = _cellEnumerable.GetEnumerator();
                        _rows.Add(_rowIndex, cellEnumerator);
                        if (_rowIndex == desiredRowIndex)
                            return true;
                    }
                }
                else
                {
                    _rowsLoadedIn = true;
                    break;
                }
            }

            cellEnumerator = null;
            return false;
        }

        public uint GetAllRows()
        {
            IEnumerator<Cell> cellEnumerator;
            while (!_rowsLoadedIn)
            {
                if (_rowEnumerator.MoveNext())
                {
                    _row = _rowEnumerator.Current;
                    _rowIndexNullable = _row.RowIndex;
                    if (_rowIndexNullable.HasValue)
                    {
                        _rowIndex = _rowIndexNullable.Value;
                        _cellEnumerable = _row.Elements<Cell>();
                        cellEnumerator = _cellEnumerable.GetEnumerator();
                        _rows.Add(_rowIndex, cellEnumerator);
                    }
                }
                else
                {
                    _rowsLoadedIn = true;
                    break;
                }
            }
            return _rowIndex;
        }

        ICellData IXlsxSheetFile.ProcessedCell(Cell cellElement, ICellIndex index)
        {
            ICellData cellData;
            if(cellElement != null)
            {
                bool hasBeenProcessed = ProcessCustomCell(cellElement, out cellData);
                if (!hasBeenProcessed)//if there is no custom cell then we will use add a simple CellDataContent
                {
                    cellData = new CellDataContent { Text = cellElement.CellValue.Text };
                }
            }
            else
            {
                cellData = new EmptyCell();
            }

            if (cellData != null && index != null)
            {
                //use the same value from the promise
                cellData.CellColumnIndex = index.CellColumnIndex;
                cellData.CellRowIndex = index.CellRowIndex;
            }

            return cellData;
        }


        /// <summary>
        /// This is for custom Cell Types like dates Cell with relations like text etc
        /// </summary>
        /// <param name="c"></param>
        /// <param name="cellData"></param>
        /// <returns></returns>
        private bool ProcessCustomCell(Cell c, out ICellData cellData)
        {
            if (c.StyleIndex != null)
            {
                int index = int.Parse(c.StyleIndex.InnerText);
                
                CellFormat cellFormat = _fileAccess.GetCellFormat(index).GetAwaiter().GetResult();
                if (cellFormat != null)
                {
                    if (ExcelStaticData.DATE_FROMAT_DICTIONARY.ContainsKey(cellFormat.NumberFormatId))
                    {
                        if (!string.IsNullOrEmpty(c.CellValue.Text))
                        {
                            if (double.TryParse(c.CellValue.Text, out double cellDouble))
                            {

                                DateTime theDate = DateTime.FromOADate(cellDouble);
                                cellData = new CellDataDate { Date = theDate, DateFormat = ExcelStaticData.DATE_FROMAT_DICTIONARY[cellFormat.NumberFormatId] };
                                return true;
                            }
                        }
                    }
                }
            }

            if (c.DataType?.Value != null && c.DataType?.Value == CellValues.SharedString)
            {
                int index = int.Parse(c.CellValue.InnerText);
                OpenXmlElement sharedStringElement = _fileAccess.GetSharedStringTableElement(index).GetAwaiter().GetResult();
                cellData = new CellDataRelation(index, sharedStringElement);
                return true;
            }
            cellData = null;
            return false;
        }

        public static string GetColumnIndexByColumnReference(StringValue columnReference)
        {
            string v = columnReference.Value;
            for (int i = v.Length - 1; i >= 0; i--)
            {
                char c = v[i];
                if (!Char.IsNumber(c))
                {
                    return v.Substring(0, i + 1);
                }

            }
            return v;
        }
    }
}
