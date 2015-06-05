/// Andl is A New Data Language. See andl.org.
///
/// Copyright © David M. Bennett 2015 as an unpublished work. All rights reserved.
///
/// If you have received this file directly from me then you are hereby granted 
/// permission to use it for personal study. For any other use you must ask my 
/// permission. Not to be copied, distributed or used commercially without my 
/// explicit written permission.
///
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Runtime {
  /// <summary>
  /// An accumulator block is maintained by each row during an aggregation calculation
  /// </summary>
  public class AccumulatorBlock {
    // offset indexing, used by def block
    public int IndexBase { get; set; }
    // temporary for result if needed later
    public TypedValue Result { get; set; }
    // direct access to accumulators, helpful for persist
    public TypedValue[] Accumulators { get { return _accumulators; } }

    TypedValue[] _accumulators;

    // access by []
    public TypedValue this[int index] {
      get { return _accumulators[IndexBase + index]; }
      set { _accumulators[IndexBase + index] = value; }
    }

    // Create accumulator block filled with nulls (to trigger default)
    public static AccumulatorBlock Create(int num) {
      return new AccumulatorBlock {
        //_accumulators = Enumerable.Repeat(TypedValue.Empty, num).ToArray()
        _accumulators = new TypedValue[num] 
      };
    }
  }
}
