using System.Data.SqlTypes;

namespace NetUtils
{
    public static class UserDefinedFunctions
    {

        /// <summary>
        ///     return extension (e.g. html) from specified input string
        /// </summary>
        /// <param name="fileExtn"
        ///     full/partial filespec (e.g. c:\dev\abc.def or c:\dev\abc. or c:\dev\abc)
        /// </param>
        /// <returns>
        ///     extension part (e.g. def or null)
        /// </returns>
        /// <remarks>
        ///     CLR function more efficient than TSQL that has no LastIndexOf function (so would need WHILE loop or REVERSE fn)
        /// </remarks>
        [Microsoft.SqlServer.Server.SqlFunction]
        public static SqlString udf_Extn(string fileExtn)
        {
            if (string.IsNullOrWhiteSpace(fileExtn))
            {
                return null;
            }
            var dot = fileExtn.LastIndexOf('.');            // determine where filename-extension delimiter comes (if any)
            return (dot < 0 || dot == fileExtn.Length - 1)
                ? null                                      // no extension (ditto null or blank extn)
                : new SqlString(fileExtn.Substring(dot + 1));
        }
    }
}
