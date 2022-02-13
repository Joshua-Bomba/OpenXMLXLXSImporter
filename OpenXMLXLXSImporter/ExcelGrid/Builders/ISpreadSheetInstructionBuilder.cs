﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenXMLXLXSImporter.ExcelGrid.Builders
{
    public  interface ISpreadSheetInstructionBuilder
    {
        ISpreadSheetInstructionKey LoadSingleCell(uint row, string cell);
    }
}