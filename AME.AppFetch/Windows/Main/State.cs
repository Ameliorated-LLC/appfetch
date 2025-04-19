using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using ReactiveUI;

namespace AME.AppFetch.Windows.Main;

public class State : ReactiveObject
{
    public static State Current = new State();
}
