﻿using Bridge;

namespace Test.BridgeIssues.N783
{
    public class App
    {
        public static void Main()
        {
            var base1 = new Base();
            var base2 = new Base();

            // Casting will be ignored
            var ignore = (Ignore)base1;

            // Default casting operation
            var dontIgnore = (DontIgnore)base2;
        }
    }

    public class Base { }

    [IgnoreCast]
    public class Ignore : Base { }

    public class DontIgnore : Base { }
}