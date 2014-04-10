using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Winter;

namespace WinterBotTests
{
    [TestClass]
    public class TwitchUserTests
    {
        
        [TestMethod]
        public void UserNameNegative()
        {
            Assert.IsFalse(TwitchUsers.IsValidUserName(""));
            Assert.IsFalse(TwitchUsers.IsValidUserName("http://hello.com"));
            Assert.IsFalse(TwitchUsers.IsValidUserName("hello.com"));
            Assert.IsFalse(TwitchUsers.IsValidUserName("one two"));
            Assert.IsFalse(TwitchUsers.IsValidUserName("one "));
            Assert.IsFalse(TwitchUsers.IsValidUserName(" one"));
        }

        [TestMethod]
        public void UserName()
        {
            Assert.IsTrue(TwitchUsers.IsValidUserName("soandso"));
            Assert.IsTrue(TwitchUsers.IsValidUserName("Soandso"));
            Assert.IsTrue(TwitchUsers.IsValidUserName("SoAndso"));
            Assert.IsTrue(TwitchUsers.IsValidUserName("SOANDSO"));
            Assert.IsTrue(TwitchUsers.IsValidUserName("so_and_so_"));
            Assert.IsTrue(TwitchUsers.IsValidUserName("so_and_so_12345"));
        }
    }
}
