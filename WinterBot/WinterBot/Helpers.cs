using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Winter
{
    public class Url
    {
        public string FullUrl { get; private set; }
        public string Extension { get; private set; }
        public string Domain { get; private set; }

        internal Url(Match match)
        {
            var groups = match.Groups;
            if (groups.Count != 4)
                throw new InvalidOperationException("Invalid match passed to Url.");

            FullUrl = groups[0].Value;
            Extension = groups[2].Value;
            Domain = groups[1].Value + Extension;
        }

        public override string ToString()
        {
            return FullUrl;
        }
    }

    public static class Extensions
    {
        static Regex s_url = new Regex(@"([\w-]+\.)+([\w-]+)(/[\w-./?%&=]*)?", RegexOptions.IgnoreCase);
        const string s_urlExtensionList = "arpa,com,edu,firm,gov,int,mil,mobi,nato,net,nom,org,store,web,me,ac,ad,ae,af,ag,ai,al,am,an,ao,aq,ar,as,at,au,aw,az,ba,bb,bd,be,bf,bg,bh,bi,bj,bm,bn,bo,br,bs,bt,bv,bw,by,bz,ca,cc,cf,cg,ch,ci,ck,cl,cm,cn,co,cr,cs,cu,cv,cx,cy,cz,de,dj,dk,dm,do,dz,ec,ee,eg,eh,er,es,et,eu,fi,fj,fk,fm,fo,fr,fx,ga,gb,gd,ge,gf,gh,gi,gl,gm,gn,gp,gq,gr,gs,gt,gu,gw,gy,hk,hm,hn,hr,ht,hu,id,ie,il,in,io,iq,ir,is,it,jm,jo,jp,ke,kg,kh,ki,km,kn,kp,kr,kw,ky,kz,la,lb,lc,li,lk,lr,ls,lt,lu,lv,ly,ma,mc,md,mg,mh,mk,ml,mm,mn,mo,mp,mq,mr,ms,mt,mu,mv,mw,mx,my,mz,na,nc,ne,nf,ng,ni,nl,no,np,nr,nt,nu,nz,om,pa,pe,pf,pg,ph,pk,pl,pm,pn,pr,pt,pw,py,qa,re,ro,ru,rw,sa,sb,sc,sd,se,sg,sh,si,sj,sk,sl,sm,sn,so,sr,st,su,sv,sy,sz,tc,td,tf,tg,th,tj,tk,tm,tn,to,tp,tr,tt,tv,tw,tz,ua,ug,uk,um,us,uy,uz,va,vc,ve,vg,vi,vn,vu,wf,ws,ye,yt,yu,za,zm,zr,zw";
        static HashSet<string> s_urlExtensions = new HashSet<string>(s_urlExtensionList.Split(','));

        public static Url[] FindUrls(this string self)
        {
            if (self.IndexOf('.') == -1)
                return null;

            var urls = from Match match in s_url.Matches(self)
                       let groups = match.Groups
                       where s_urlExtensions.Contains(groups[groups.Count - 2].Value)
                       select new Url(match);

            return urls.ToArray();
        }

        public static bool IsRegex(this string self)
        {
            bool firstQ = true;

            foreach (char c in self)
            {
                if ('a' <= c && c <= 'z')
                    continue;

                if ('A' <= c && c <= 'Z')
                    continue;

                if ('0' <= c && c <= '9')
                    continue;

                if (c == '.' || c == '/' || c == '_' || c == '-' || c == '=' || c == '&' || c == '%')
                    continue;

                // Heuristic, not going to be right all the time
                if (c == '?' && firstQ)
                {
                    firstQ = false;
                    continue;
                }

                return true;
            }

            return false;
        }

        public static TimeSpan Elapsed(this DateTime self)
        {
            return DateTime.Now - self;
        }

        public static IEnumerable<T> Enumerate<T>(this ConcurrentQueue<T> self)
        {
            T value;
            while (self.TryDequeue(out value))
                yield return value;
        }


        public static bool ParseBool(this string self, ref bool result)
        {
            if (bool.TryParse(self, out result))
                return true;

            result = true;
            if (self.Equals("true", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("t", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("yes", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("y", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("1", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("enable", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("enabled", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("on", StringComparison.CurrentCultureIgnoreCase))
                return true;

            result = false;
            if (self.Equals("false", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("f", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("no", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("n", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("0", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("disable", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("disabled", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("off", StringComparison.CurrentCultureIgnoreCase))
                return true;

            return false;
        }
    }
}
