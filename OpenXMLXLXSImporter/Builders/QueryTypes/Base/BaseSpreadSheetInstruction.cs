﻿using Nito.AsyncEx;
using OpenXMLXLSXImporter.CellData;
using OpenXMLXLSXImporter.Indexers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenXMLXLSXImporter.Builders
{
    public abstract class BaseSpreadSheetInstruction : ISpreadSheetInstruction
    {
        protected AsyncManualResetEvent _mre;
        public BaseSpreadSheetInstruction() {
            _mre = new AsyncManualResetEvent(false);
        }

        public abstract bool IndexedByRow { get; }

        async IAsyncEnumerable<ICellData> ISpreadSheetInstruction.GetResults()
        {
            await _mre.WaitAsync();
            IAsyncEnumerator<ICellIndex> cells = GetResults().GetAsyncEnumerator();
            while(await cells.MoveNextAsync())
            {
                yield return await GetCellData(cells.Current);
            }
        }

        public static async Task<ICellData> GetCellData(ICellIndex i)
        {
            if (i is IFutureCell fs)
            {
                return await fs.GetData();
            }
            else if(i is ICellData cd)
            {
                return cd;
            }
            throw new InvalidOperationException();
        }


        protected abstract IAsyncEnumerable<ICellIndex> GetResults();

        async Task  ISpreadSheetInstruction.EnqueCell(IIndexer indexer)
        {
            await EnqueCell(indexer);
            _mre.Set();
        }


        protected abstract Task EnqueCell(IIndexer indexer);
    }
}
