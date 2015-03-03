Terminology
====

Terminology is frequently a source of confusion. Often times, terms have different 
meanings depending on the context, different terms are used to refer to the same 
concept, and finally not everyone agrees on any of this. The terminology described here 
is scoped to F# and the .NET Framework. The goal is to briefly introduce high level
concepts and hopefully alleviate some confusion.

## Thread

A thread in this scope refers to a [managed thread](https://msdn.microsoft.com/en-us/library/6kac2kdh(v=vs.110).aspx). 
A thread is a basic unit to which an OS allocates CPU resources. A *managed* thread usually maps directly to
an OS thread, however it is possible for a CLR host to override this behavior. A thread has a corresponding context 
consisting of a pointer to the code being executed as well as stack and register state. 

A thread can be viewed as a total ordering of instructions. Instructions executed by different threads
are partially ordered by causality relationships - in some cases its impossible to tell which ran
before the other. Much of the difficulty in multi-threaded code can be attributed to this lack of 
information about ordering.

## Context Switch

A context switch occurs when the OS scheduler decides to change the thread to which it allocates CPU resources.
In order to do this, it must save the context of the thread which is giving up use of the CPU and reconstitute
the context of the thread which is next in line. Context switches occur for a number of reasons. 
It occurs naturally as part of preemptive scheduling - threads run for a *time slice* or *quantum* until another
thread is given a chance to run. Another reason is when a thread explicitly yields its time slice, such as with a call to
`Thread.Sleep`.

## Synchronous vs. Asynchronous

An operation is synchronous if the caller must wait for it to complete before making progress. 
More specifically, the calling *thread* may *block* until the synchronous operation is complete. 
Note that a CPU-bound task exhibits behavior similar to blocking.

An operation is asynchronous if the request to begin the operation and the result of the operation can
be delivered through different channels. This provides a convenient mechanism to encapsulate waiting. In other words, 
an asynchronous operations decouples the means of sending the request from the means of receiving a response. 

This decoupling allows one to in turn decouple the *logical* notion of an operation from the *physical*
details of how it is executed. For example, an asynchronous operation to download a web page is a single 
*logical* operation. Due to its asynchronous nature however, the underlying implementation can start the 
operation on one thread and then deliver the completion notification through a different thread
(such as an IO completion thread managed by the ThreadPool). In the meantime, the calling thread is free
to perform other work. In fact, the completion notification can even be handled by the same thread
as the calling thread.

By contrast, a synchronous operation will use the calling thread's context to deliver the completion
notification. If the work to be performed is small enough, this can be very efficient. If however the 
operation is long running, the OS will perform a context switch to allow other threads to proceed, and 
then another context switch to resume the calling thread. Note that synchrony can be viewed as a special
form of asynchrony.

In F# asynchrony is represented by the `Async` type.

## Blocking vs. Non-blocking

A thread is [blocked](http://www.albahari.com/threading/part2.aspx#_Blocking) when its execution
is paused as it waits for some operation to complete (receiving IO, a lock being released, etc). Once the operation completes, 
the OS will schedule the thread to resume and continue where it left off.

A non-blocking operation is one that does not prevent the calling thread from making progress. In other words,
once an non-blocking operation is started, the calling thread is free to perform other work, such as starting
yet another operation.

It is important to remember that when a thread is blocked, the CPU and the system as a whole can still do other work. 
The issue with blocking is that the specific thread which is blocked can't do other work and the OS must 
use resources for thread's context so that it can context switch continue where it left off once the operating being waited on is complete. 
Since managed threads have a relatively high cost (by default, a .NET thread is allocated 1mb of stack space), this can 
lead to inefficiencies. Non-blocking operations allow one to make more efficient use of system resources.

## Further Reading

 * [Managed Threading](https://msdn.microsoft.com/en-us/library/3e8s7xdd%28v=vs.110%29.aspx)
 * [Threading in C#](http://www.albahari.com/threading/)
 * [The Art of Multiprocessor Programming](http://www.amazon.com/Art-Multiprocessor-Programming-Revised-Reprint/dp/0123973376/)