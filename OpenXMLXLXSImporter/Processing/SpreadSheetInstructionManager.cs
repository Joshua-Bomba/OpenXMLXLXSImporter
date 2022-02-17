﻿using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Nito.AsyncEx;
using OpenXMLXLSXImporter.Builders;
using OpenXMLXLSXImporter.CellData;
using OpenXMLXLSXImporter.FileAccess;
using OpenXMLXLSXImporter.Indexers;
using OpenXMLXLSXImporter.Utils;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenXMLXLSXImporter.Processing
{
    /// <summary>
    /// this will handle accessing the grid and iterateing over the spreadsheetgrid
    /// interate through all the cells for an entire column
    /// interate through all the cells for an entire row
    /// interate through all the cells
    /// </summary>
    public class SpreadSheetInstructionManager : ISpreadSheetIndexersLock, IDisposable
    {
        private IXlsxDocumentFilePromise _fileAccessPromise;
        private ISheetProperties _sheetProperties;

        private Sheet _sheet;
        private WorksheetPart _workbookPart;
        private Worksheet _worksheet;
        private SheetData _sheetData;

        private Task _loadSpreadSheetData;

        private RowIndexer _rows;
        private ColumnIndexer _columns;
        private AsyncLock _accessorLock = new AsyncLock();
        private List<IIndexer> _indexers;

        private ChunkableBlockingCollection<ICellProcessingTask> _loadQueueManager;
        private SpreadSheetDequeManager _dequeuer;

        AsyncLock ISpreadSheetIndexersLock.IndexerLock => _accessorLock;

        void ISpreadSheetIndexersLock.AddIndexer(IIndexer a)
        {
            _indexers.Add(a);
        }

        void ISpreadSheetIndexersLock.Spread(IIndexer a, ICellIndex b)
        {
            foreach(IIndexer i in _indexers)
            {
                if (i != a)
                    i.Spread(b);
            }
        }

        void ISpreadSheetIndexersLock.EnqueCell(ICellProcessingTask cell)
        {
            _loadQueueManager.Enque(cell);
        }

        public SpreadSheetInstructionManager(IXlsxDocumentFilePromise fileAccess, ISheetProperties sheetProperties)
        {
            _fileAccessPromise = fileAccess;
            _sheetProperties = sheetProperties;
            _indexers = new List<IIndexer>();
            _dequeuer = new SpreadSheetDequeManager();
            _loadQueueManager = new ChunkableBlockingCollection<ICellProcessingTask>(_dequeuer);

            _loadSpreadSheetData = Task.Run(LoadSpreadSheetData);

            _rows = new RowIndexer(this);
            _columns = new ColumnIndexer(this);
        }

        protected async Task LoadSpreadSheetData()
        {
            IXlsxDocumentFile fileAccess = await _fileAccessPromise.GetLoadedFile();
            _sheet = fileAccess.GetSheet(_sheetProperties.Sheet);
            _workbookPart = fileAccess.WorkbookPart.GetPartById(_sheet.Id) as WorksheetPart;
            _worksheet = _workbookPart.Worksheet;
            _sheetData = _worksheet.Elements<SheetData>().First();
            try
            {
                IEnumerable<Row> rowsEnumerable = _sheetData.Elements<Row>();
                IEnumerator<Row> rowEnumerator = rowsEnumerable.GetEnumerator();
                IDictionary<uint,IEnumerator<Cell>> rows;//store the raw rows
               // if (rowsEnumerable.TryGetNonEnumeratedCount(out int count))//does look like in my testing the count is not determind
                rows = new Dictionary<uint, IEnumerator<Cell>>();//lets user a dictionary in this case
                Row row;
                Cell cell;
                UInt32Value rowIndexNullable;
                uint rowIndex;
                uint desiredRowIndex;
                bool rowsLoadedIn = false;

                IEnumerable<Cell> cellEnumerable;
                IEnumerator<Cell> cellEnumerator;
                

                while (true)
                {
                    ICellProcessingTask t = _loadQueueManager.Take();
                    desiredRowIndex = t.CellRowIndex;

                    //this will only load in up to the row we need
                    //if we try loading in a row that does not exist then we will load all of them in
                    //I'm assuming that data from here might still be in the file and we don't want to read things we don't need
                    while (!rowsLoadedIn && !rows.ContainsKey(desiredRowIndex))
                    {
                        if(rowEnumerator.MoveNext())
                        {
                            row = rowEnumerator.Current;
                            rowIndexNullable = row.RowIndex;
                            if (rowIndexNullable.HasValue)
                            {
                                rowIndex = rowIndexNullable.Value;
                                cellEnumerable = row.Elements<Cell>();
                                cellEnumerator = cellEnumerable.GetEnumerator();
                                rows.Add(rowIndex, cellEnumerator);
                                if (rowIndex == desiredRowIndex)
                                    break;
                            }
                        }
                        else
                        {
                            rowsLoadedIn = true;
                            break;
                        }
                    }

                    
                    if(rows.ContainsKey(desiredRowIndex))
                    {
                        string columnIndex = t.CellColumnIndex;
                        cellEnumerator = rows[desiredRowIndex];
                        bool cellsLoadedIn = false;
                        string currentIndex;
                        do
                        {
                            if(cellEnumerator.MoveNext())
                            {
                                cell = cellEnumerator.Current;
                                currentIndex = XlsxDocumentFile.GetColumnIndexByColumnReference(cell.CellReference);
                                if(currentIndex != columnIndex)
                                {
                                    _dequeuer.AddDeferredCell(new DeferredCell(desiredRowIndex, currentIndex, cell));
                                }
                            }
                            else
                            {
                                currentIndex = null;
                                cellsLoadedIn = true;
                            }
                        } while (!cellsLoadedIn&&currentIndex != columnIndex);

                        if(currentIndex == columnIndex)
                        {
                            throw new NotImplementedException();//need to handle this step now which is loading in the actual data
                        }
                        else
                        {
                            //This Cell Does not exist
                            t.Resolve(null);
                        }

                    }
                    else
                    {
                        //if we reached the end of the file and the row does not exist then what were trying to get does not exist
                        t.Resolve(null);
                    }


                }
            }
            catch (InvalidOperationException ex)
            {
                //queue is finished
            }

        }

        public void Dispose()
        {
            _loadSpreadSheetData.Wait();
        }

        public async Task ProcessInstruction(ISpreadSheetInstruction spreadSheetInstruction)
        {
            if (spreadSheetInstruction.ByColumn)
            {
                await _columns.ProcessInstruction(spreadSheetInstruction);
            }
            else
            {
                await _rows.ProcessInstruction(spreadSheetInstruction);
            }
        }

        //public async Task Add(ICellData cellData)
        //{
        //    using(await _lockRow.LockAsync())
        //    {
        //        if (!_rows.ContainsKey(cellData.CellRowIndex))
        //        {
        //            _rows[cellData.CellRowIndex] = new RowIndexer();
        //        }

        //        _rows[cellData.CellRowIndex].Add(cellData);
        //        if(_listeners != null)
        //        {
        //            //intresting so this will call each method and won't await till it's finished calling all of them
        //            //pretty handy neat pattern
        //            await Task.WhenAll(_listeners.Select(x => x.NotifyAsync(cellData)));
        //        }
        //    }
        //    using (await _lockColumn.LockAsync())
        //    {
        //        if (!_columns.ContainsKey(cellData.CellColumnIndex))
        //        {
        //            _columns[cellData.CellColumnIndex] = new ColumnIndexer(this);
        //        }

        //        _columns[cellData.CellColumnIndex].Add(cellData);
        //    }
        //}
    }
}