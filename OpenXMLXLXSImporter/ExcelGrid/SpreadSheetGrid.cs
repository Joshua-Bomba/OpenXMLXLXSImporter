﻿using Nito.AsyncEx;
using OpenXMLXLXSImporter.CellData;
using OpenXMLXLXSImporter.ExcelGrid.Indexers;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenXMLXLXSImporter.ExcelGrid
{
    /// <summary>
    /// this will handle accessing the grid and iterateing over the spreadsheetgrid
    /// interate through all the cells for an entire column
    /// interate through all the cells for an entire row
    /// interate through all the cells
    /// </summary>
    public class SpreadSheetGrid : IAsyncEnumerable<ICellData>
    {
        ISheetProperties _sheet;
        private Dictionary<uint, RowIndexer> _rows;
        private Dictionary<string, ColumnIndexer> _columns;
        private AsyncCollection<ICellData> _cellEnumeratorCollection;
        private ManualResetEvent _allLoaded;
        private object _lockRow = new object();
        private object _lockColumn = new object();
        public SpreadSheetGrid(ISheetProperties sheet)
        {
            _sheet = sheet;
            sheet.SetCellGrid(this);
            _allLoaded = new ManualResetEvent(false);
            _rows = new Dictionary<uint, RowIndexer>();
            _columns = new Dictionary<string, ColumnIndexer>();
            _cellEnumeratorCollection = new AsyncCollection<ICellData>();
        }

        /// <summary>
        /// this will return a paticular cell if it's avaliable
        /// </summary>
        /// <param name="row"></param>
        /// <param name="cell"></param>
        /// <returns></returns>
        public bool TryFetchCell(uint row, string cell)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// this will return a paticular row and cell if it's not avaliable it will wait till it's loaded or the timeout is reached
        /// </summary>
        /// <param name="rowIndex"></param>
        /// <param name="cellIndex"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public async Task<ICellData> FetchCell(uint rowIndex, string cellIndex, int timeout = -1)
        {
            ICellData cell = null;
            await Task.Run(() =>
            {
                ManualResetEvent e = null;
                lock (_lockRow)//honestly this multi threading crap is probably unessary unless you havea massive excel document but whatever this is how I wrote it
                {
                    if (!_rows.ContainsKey(rowIndex))
                    {
                        _rows[rowIndex] = new RowIndexer();
                        e = _rows[rowIndex].WaitTillAvaliable(cellIndex);
                    }
                    else
                    {
                        if (_rows[rowIndex].TryAndGetCell(cellIndex, out ICellData d))
                        {
                            cell = d;
                            return;
                        }
                        else
                        {
                            e = _rows[rowIndex].WaitTillAvaliable(cellIndex);
                        }
                    }

                }
                if (e.WaitOne(timeout))
                {
                    lock (_lockRow)
                    {
                        if (_rows[rowIndex].TryAndGetCell(cellIndex, out ICellData d))
                        {
                            cell = d;
                            return;
                        }
                    }
                }

            });
            return cell;
        }


        /// <summary>
        /// This is for the ExcelImporter to add cells after they have been parsed
        /// </summary>
        /// <param name="cellData"></param>
        public async Task Add(ICellData cellData)
        {
            Task t1 = Task.Run(() =>
            {
                lock (_lockRow)
                {
                    if (!_rows.ContainsKey(cellData.CellRowIndex))
                    {
                        _rows[cellData.CellRowIndex] = new RowIndexer();
                    }

                    _rows[cellData.CellRowIndex].Add(cellData);
                }
            });
            Task t2 = Task.Run(() =>
            {
                lock (_lockColumn)
                {
                    if (!_columns.ContainsKey(cellData.CellColumnIndex))
                    {
                        _columns[cellData.CellColumnIndex] = new ColumnIndexer();
                    }

                    _columns[cellData.CellColumnIndex].Add(cellData);
                }
            });

            await _cellEnumeratorCollection.AddAsync(cellData);
            await t1;
            await t2;
        }

        /// <summary>
        /// Excel Import is finished loading we need to notify anything that is wait that today is not there day 
        /// and there not getting any data
        /// </summary>
        public void FinishedLoading()
        {
            _allLoaded.Set();
            //TODO: notify the FetchCell to continue 
        }

        public class CellEnumerator : IAsyncEnumerator<ICellData>
        {
            private SpreadSheetGrid _ssg;
            private ICellData _current;
            public CellEnumerator(SpreadSheetGrid ssg)
            {
                _ssg = ssg;
                _current = null;
            }

            public ICellData Current => _current;

            public async ValueTask DisposeAsync()
            {

            }

            public async ValueTask<bool> MoveNextAsync()
            {
                try
                {
                    ICellData c = await _ssg._cellEnumeratorCollection.TakeAsync();
                    if(c != null)
                    {
                        _current = c;
                        return true;
                    }
                }
                catch
                {

                }
                return false;                
            }
        }

        public CellEnumerator _singleInstanceOnly;

        public IAsyncEnumerator<ICellData> GetAsyncEnumerator(CancellationToken cancellationToken = default) => _singleInstanceOnly ??= new CellEnumerator(this);
    }
}
