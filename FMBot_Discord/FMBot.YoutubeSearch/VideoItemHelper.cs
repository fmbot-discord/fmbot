// This file is part of YoutubeSearch.
//
// YoutubeSearch is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// YoutubeSearch is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with YoutubeSearch. If not, see<http://www.gnu.org/licenses/>.

namespace YoutubeSearch
{
    public class VideoItemHelper
    {
        public static string cull(string strSource, string strStart, string strEnd)
        {
			int Start, End;

            if (strSource.Contains(strStart) && strSource.Contains(strEnd))
            {
                Start = strSource.IndexOf(strStart, 0) + strStart.Length;
                End = strSource.IndexOf(strEnd, Start);

				int val = (End > Start ? End - Start : Start - End);
				if (val < 1 || val >= strSource.Length)
					return "";

				return strSource.Substring(Start, val);
            }
            else
            {
                return "";
            }
        }
    }
}
