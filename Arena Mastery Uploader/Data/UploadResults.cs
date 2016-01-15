using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArenaMasteryUploader.Data
{
    public enum UploadResults
    {
        Success,
        LoginFailedCredentialsWrong,        
        LoginFailedUnknownError,
        SubmittingArenaRunFailedUnknownError,
        ConnectionError
    }
}
