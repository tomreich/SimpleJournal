/*
 * Copyright (c) 2017 Tom Reich
 * 
 * Licensed under the Microsoft Public License (MS-PL) (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *  https://msdn.microsoft.com/en-us/library/ff649456.aspx
 *  or
 *  https://opensource.org/licenses/MS-PL
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimpleJournal
{
    class EntryFile
    {
        private DateTime _fileDateTime = DateTime.MinValue;
        public string FileName { get; set; }
        public DateTime FileDateTime
        {
            get
            {
                if (_fileDateTime != DateTime.MinValue)
                    return _fileDateTime;

                DateTime parsed;
                _fileDateTime = DateTime.TryParse(FileName.Replace(".rtf", string.Empty).Replace('_', ':'), out parsed) ? parsed : DateTime.MinValue;
                return _fileDateTime;
            }
        }

        public DateTime FileDate
        {
            get { return FileDateTime != DateTime.MinValue ? new DateTime(FileDateTime.Year, FileDateTime.Month, FileDateTime.Day) : DateTime.MinValue; }
        }

        public override string ToString()
        {
            return FileDateTime.ToString("yyyy-MM-dd hh:mm:ss tt");
        }
    }
}
