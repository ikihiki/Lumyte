namespace Lumyte.Composition;

/// <summary>Creates a host-defined composition from 1 typed argument.</summary>
/// <typeparam name="TArgs">The type of argument 1.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<TArgs, TComposition>(TArgs arg1);

/// <summary>Creates a host-defined composition from 2 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, TComposition>(T1 arg1, T2 arg2);

/// <summary>Creates a host-defined composition from 3 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, TComposition>(T1 arg1, T2 arg2, T3 arg3);

/// <summary>Creates a host-defined composition from 4 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);

/// <summary>Creates a host-defined composition from 5 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);

/// <summary>Creates a host-defined composition from 6 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);

/// <summary>Creates a host-defined composition from 7 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);

/// <summary>Creates a host-defined composition from 8 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);

/// <summary>Creates a host-defined composition from 9 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9);

/// <summary>Creates a host-defined composition from 10 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="T10">The type of argument 10.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <param name="arg10">Argument 10, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10);

/// <summary>Creates a host-defined composition from 11 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="T10">The type of argument 10.</typeparam>
/// <typeparam name="T11">The type of argument 11.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <param name="arg10">Argument 10, passed without copying or validation.</param>
/// <param name="arg11">Argument 11, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11);

/// <summary>Creates a host-defined composition from 12 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="T10">The type of argument 10.</typeparam>
/// <typeparam name="T11">The type of argument 11.</typeparam>
/// <typeparam name="T12">The type of argument 12.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <param name="arg10">Argument 10, passed without copying or validation.</param>
/// <param name="arg11">Argument 11, passed without copying or validation.</param>
/// <param name="arg12">Argument 12, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12);

/// <summary>Creates a host-defined composition from 13 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="T10">The type of argument 10.</typeparam>
/// <typeparam name="T11">The type of argument 11.</typeparam>
/// <typeparam name="T12">The type of argument 12.</typeparam>
/// <typeparam name="T13">The type of argument 13.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <param name="arg10">Argument 10, passed without copying or validation.</param>
/// <param name="arg11">Argument 11, passed without copying or validation.</param>
/// <param name="arg12">Argument 12, passed without copying or validation.</param>
/// <param name="arg13">Argument 13, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13);

/// <summary>Creates a host-defined composition from 14 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="T10">The type of argument 10.</typeparam>
/// <typeparam name="T11">The type of argument 11.</typeparam>
/// <typeparam name="T12">The type of argument 12.</typeparam>
/// <typeparam name="T13">The type of argument 13.</typeparam>
/// <typeparam name="T14">The type of argument 14.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <param name="arg10">Argument 10, passed without copying or validation.</param>
/// <param name="arg11">Argument 11, passed without copying or validation.</param>
/// <param name="arg12">Argument 12, passed without copying or validation.</param>
/// <param name="arg13">Argument 13, passed without copying or validation.</param>
/// <param name="arg14">Argument 14, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14);

/// <summary>Creates a host-defined composition from 15 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="T10">The type of argument 10.</typeparam>
/// <typeparam name="T11">The type of argument 11.</typeparam>
/// <typeparam name="T12">The type of argument 12.</typeparam>
/// <typeparam name="T13">The type of argument 13.</typeparam>
/// <typeparam name="T14">The type of argument 14.</typeparam>
/// <typeparam name="T15">The type of argument 15.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <param name="arg10">Argument 10, passed without copying or validation.</param>
/// <param name="arg11">Argument 11, passed without copying or validation.</param>
/// <param name="arg12">Argument 12, passed without copying or validation.</param>
/// <param name="arg13">Argument 13, passed without copying or validation.</param>
/// <param name="arg14">Argument 14, passed without copying or validation.</param>
/// <param name="arg15">Argument 15, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15);

/// <summary>Creates a host-defined composition from 16 typed arguments.</summary>
/// <typeparam name="T1">The type of argument 1.</typeparam>
/// <typeparam name="T2">The type of argument 2.</typeparam>
/// <typeparam name="T3">The type of argument 3.</typeparam>
/// <typeparam name="T4">The type of argument 4.</typeparam>
/// <typeparam name="T5">The type of argument 5.</typeparam>
/// <typeparam name="T6">The type of argument 6.</typeparam>
/// <typeparam name="T7">The type of argument 7.</typeparam>
/// <typeparam name="T8">The type of argument 8.</typeparam>
/// <typeparam name="T9">The type of argument 9.</typeparam>
/// <typeparam name="T10">The type of argument 10.</typeparam>
/// <typeparam name="T11">The type of argument 11.</typeparam>
/// <typeparam name="T12">The type of argument 12.</typeparam>
/// <typeparam name="T13">The type of argument 13.</typeparam>
/// <typeparam name="T14">The type of argument 14.</typeparam>
/// <typeparam name="T15">The type of argument 15.</typeparam>
/// <typeparam name="T16">The type of argument 16.</typeparam>
/// <typeparam name="TComposition">The host-defined composition type.</typeparam>
/// <param name="arg1">Argument 1, passed without copying or validation.</param>
/// <param name="arg2">Argument 2, passed without copying or validation.</param>
/// <param name="arg3">Argument 3, passed without copying or validation.</param>
/// <param name="arg4">Argument 4, passed without copying or validation.</param>
/// <param name="arg5">Argument 5, passed without copying or validation.</param>
/// <param name="arg6">Argument 6, passed without copying or validation.</param>
/// <param name="arg7">Argument 7, passed without copying or validation.</param>
/// <param name="arg8">Argument 8, passed without copying or validation.</param>
/// <param name="arg9">Argument 9, passed without copying or validation.</param>
/// <param name="arg10">Argument 10, passed without copying or validation.</param>
/// <param name="arg11">Argument 11, passed without copying or validation.</param>
/// <param name="arg12">Argument 12, passed without copying or validation.</param>
/// <param name="arg13">Argument 13, passed without copying or validation.</param>
/// <param name="arg14">Argument 14, passed without copying or validation.</param>
/// <param name="arg15">Argument 15, passed without copying or validation.</param>
/// <param name="arg16">Argument 16, passed without copying or validation.</param>
/// <returns>The composition returned by the invoked delegate.</returns>
public delegate TComposition CompositionTemplate<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TComposition>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, T16 arg16);
