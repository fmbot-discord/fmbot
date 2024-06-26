using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.AppleMusic;

public class AppleMusicApi
{
    private readonly HttpClient _client;

    public AppleMusicApi(HttpClient client)
    {
        this._client = client;
    }

    
}
