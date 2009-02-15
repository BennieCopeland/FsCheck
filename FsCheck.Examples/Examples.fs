#light

open FsCheck
open System
open System.Threading
open System.Reflection
open Microsoft.FSharp.Reflection
open Microsoft.FSharp.Collections
open System.Collections.Generic;



//---------timeout---------

let prop_timeout (a:int) = 
    lazy
        if a>10 then
            while true do 
                Thread.Sleep(1000)
            false
        else 
            true
    |> within 2000

quickCheck prop_timeout

//-------labelling sub properties------


let prop_Label (x:int) = 
    "Always false" @| false
    .&. "Always true" @| (x > 0 ==> (abs x - x = 0))
quickCheck prop_Label

let propMul (n: int, m: int) =
  let res = n*m
  sprintf "evidence = %i" res @| Prop.And(
    "div1" @| (m <> 0 ==> lazy (res / m = n)),
    "div2" @| (n <> 0 ==> lazy (res / n = m)),
    "lt1"  @| (res > m),
    "lt2"  @| (res > n))
quickCheck propMul

let propOr (n:int) (m:int) =
    let res = n - m
    "Positive"  @| (res > 0) .|.
    "Negative"  @| (res < 0) .|.
    "Zero"      @| (res = 0)
    |> classify (res > 0) "Positive"
    |> classify (res < 0) "Negative"
    
quickCheck propOr

let complexProp (m: int) (n: int) =
  let res = n + m
  (res >= m)    |@ "result > #1" .&.
  (res >= n)    |@ "result > #2" .&.
  (res < m + n) |@ "result not sum"
quickCheck complexProp



//---- recursive record types----------
type A = { A : A }
let rec a : A = { A = a } 
let private prop1 (a : A) = true
//quickCheck prop1 produces a warning


//----------------Function printing and shrinking------
let propMap (Function (_,f)) (l:list<int>) =
    not l.IsEmpty ==>
    lazy (List.map f l = ((*f*)(List.hd l)) :: (List.map f (List.tl l)))
quickCheck propMap

//------alternative to using forAll-----

//to force registrating of type classes and FsCheck arbitrary generators. Also happens automatically whenever a function
//from the runner module is called.
do init.Value

type NonNegativeInt = NonNegative of int
type NonZeroInt = NonZero of int
type PositiveInt = Positive of int
//type BoundedInt = Bounded of int

type ArbitraryModifiers =
    static member NonNegativeInt() =
        { new Arbitrary<NonNegativeInt>() with
            override x.Arbitrary = arbitrary |> fmapGen (NonNegative << abs)
            override x.CoArbitrary (NonNegative i) = coarbitrary i
            override x.Shrink (NonNegative i) = shrink i |> Seq.filter ((<) 0) |> Seq.map NonNegative }
    static member NonZeroInt() =
        { new Arbitrary<NonZeroInt>() with
            override x.Arbitrary = arbitrary |> suchThat ((<>) 0) |> fmapGen NonZero 
            override x.CoArbitrary (NonZero i) = coarbitrary i
            override x.Shrink (NonZero i) = shrink i |> Seq.filter ((=) 0) |> Seq.map NonZero }
    static member PositiveInt() =
        { new Arbitrary<PositiveInt>() with
            override x.Arbitrary = arbitrary |> suchThat ((<>) 0) |> fmapGen (Positive << abs) 
            override x.CoArbitrary (Positive i) = coarbitrary i
            override x.Shrink (Positive i) = shrink i |> Seq.filter ((<=) 0) |> Seq.map Positive }

//let (|BoundedAP|_|) (l,h) b = match b with (Bounded i) -> Some (forAll <| choose (l,h))
//let prop_Bounded (BoundedAP (0,5) bound) = bound (fun i -> 0<= i && i<=5)

registerGenerators<ArbitraryModifiers>()

let prop_NonNeg (NonNegative i) = i >= 0
quickCheckN "NonNeg" prop_NonNeg

let prop_NonZero (NonZero i) = i <> 0
quickCheckN "NonZero" prop_NonZero

let prop_Positive (Positive i) = i > 0
quickCheckN "Pos" prop_Positive

//-----Ganesh's tests------------------


let private withPositiveInteger (p : int -> 'a) = fun n -> n <> 0 ==> lazy (p (abs n))

let testProp = withPositiveInteger ( fun x -> x > 0 |> classify true "bla"  )
quickCheck testProp

let testProp2 = withPositiveInteger ( fun x -> withPositiveInteger (fun y -> x + y > 0  ))
quickCheck testProp2

let blah (s:string) = if s = "" then raise (new System.Exception("foo")) else s.Length > 3

let private withNonEmptyString (p : string -> 'a) = forAll (oneof (List.map gen.Return [ "A"; "AA"; "AAA" ])) p

quickCheck (fun () -> withNonEmptyString blah)

//test exceptions
let prop_Exc() = forAllShrink (resize 100 arbitrary) shrink (fun (s:string) -> failwith "error")
quickCheckN "prop_Exc" prop_Exc


quickCheck <|
    (fun () -> throws<DivideByZeroException,_> (lazy (raise <| DivideByZeroException())))


//-----------------test reflective shrinking--------

type RecordStuff<'a> = { Yes:bool; Name:'a; NogIets:list<int*char> }

quickCheck <| 
    (fun () -> forAllShrink (resize 100 arbitrary) shrink (fun (s:RecordStuff<string>) -> s.Yes)) 

type Recursive<'a> = Void | Leaf of 'a | Branch of Recursive<'a> * 'a * Recursive<'a>

quickCheck <| 
    (fun () -> forAllShrink (resize 100 arbitrary) shrink (fun (s:Recursive<string>) -> 
    match s with  Branch _ -> false | _ -> true)) 

type Simple = Void | Void2 | Void3 | Leaf of int | Leaf2 of string * int *char * float

//should yield a simplified Leaf2
quickCheck <| 
    (fun () -> forAllShrink (resize 100 arbitrary) shrink (fun (s:Simple) -> 
    match s with Leaf2 _ -> false |  _ -> true)) 

//should yield a Void3
quickCheck <| 
    (fun () -> forAllShrink (resize 100 arbitrary) shrink (fun (s:Simple) -> 
    match s with Leaf2 _ -> false | Void3 -> false |  _ -> true)) 


quickCheck <| (fun () -> forAllShrink (resize 100 arbitrary) shrink (fun i -> (-10 < i && i < 0) || (0 < i) && (i < 10 )))
quickCheck (fun opt -> match opt with None -> false | Some b  -> b  )
quickCheck (fun opt -> match opt with None -> true | Some n when n<0 -> false | Some n when n >= 0 -> true )

let prop_RevId' (xs:list<int>) (x:int) = if (xs.Length > 2) && (x >10) then false else true
quickCheck prop_RevId'


//-------A Simple Example----------
let prop_RevRev (xs:list<int>) = List.rev(List.rev xs) = xs
quickCheck prop_RevRev

let prop_RevId (xs:list<int>) = List.rev xs = xs
quickCheckN "RevId" prop_RevId

//------Grouping properties--------
type ListProperties =
    static member RevRev xs = prop_RevRev xs
    static member RevId xs = prop_RevId xs
quickCheckAll (typeof<ListProperties>)

//-----Properties----------------
let prop_RevRevFloat (xs:list<float>) = List.rev(List.rev xs) = xs
quickCheck prop_RevRevFloat

//Conditional Properties
let rec private ordered xs = match xs with
                             | [] -> true
                             | [x] -> true
                             | x::y::ys ->  (x <= y) && ordered (y::ys)
let rec insert x xs = match xs with
                      | [] -> [x]
                      | c::cs -> if x <= c then x::xs else c::(insert x cs)                      
let prop_Insert (x:int) xs = ordered xs ==> ordered (insert x xs)
quickCheck prop_Insert

//Lazy properties
let prop_Eager a = a <> 0 ==> (1/a = 1/a)
let prop_Lazy a = a <> 0 ==> (lazy (1/a = 1/a))
quickCheck prop_Eager
quickCheck prop_Lazy

//Counting trivial cases
let prop_InsertTrivial (x:int) xs = 
    ordered xs ==> (ordered (insert x xs))
    |> trivial (List.length xs = 0)
quickCheck prop_InsertTrivial

//Classifying test cases
let prop_InsertClassify (x:int) xs = 
    ordered xs ==> (ordered (insert x xs))
    |> classify (ordered (x::xs)) "at-head"
    |> classify (ordered (xs @ [x])) "at-tail" 
quickCheck prop_InsertClassify
    
//Collecting data values
let prop_InsertCollect (x:int) xs = 
    ordered xs ==> (ordered (insert x xs))
        |> collect (List.length xs)
quickCheck prop_InsertCollect

//Combining observations
let prop_InsertCombined (x:int) xs = 
    ordered xs ==> (ordered (insert x xs))
        |> classify (ordered (x::xs)) "at-head"
        |> classify (ordered (xs @ [x])) "at-tail"
        |> collect (List.length xs)
quickCheck prop_InsertCombined

//--------Test Data Generators----------
let private chooseFromList xs = 
    gen {   let! i = choose (0, List.length xs-1) 
            return (List.nth xs i) }

//to generate a value out of a generator:
//generate <size> <seed> <generator>
//generate 0 (Random.newSeed()) (chooseFromList [1;2;3])

//Choosing between alternatives
let private chooseBool = 
    oneof [ gen { return true }; gen { return false } ]
    
let private chooseBool2 = 
    frequency [ (2, gen { return true }); (1, gen { return false })]

//The size of test data
let matrix gen = sized <| fun s -> resize (s|>float|>sqrt|>int) gen

//Generating Recusrive data types
type Tree = Leaf of int | Branch of Tree * Tree

let rec private unsafeTree() = 
    oneof [ liftGen (Leaf) arbitrary; 
            liftGen2 (fun x y -> Branch (x,y)) (unsafeTree()) (unsafeTree())]

let tree =
    let rec tree' s = 
        match s with
            | 0 -> liftGen (Leaf) arbitrary
            | n when n>0 -> 
            let subtree() = tree' (n/2)
            oneof [ liftGen (Leaf) arbitrary; 
                    liftGen2 (fun x y -> Branch (x,y)) (subtree()) (subtree())]
            | _ -> raise(ArgumentException"Only positive arguments are allowed")
    sized tree'

//Generating functions
let rec cotree t = 
    match t with
       | (Leaf n) -> variant 0 << coarbitrary n
       | (Branch (t1,t2)) -> variant 1 << cotree t1 << cotree t2


//Default generators by type
type Box<'a> = Whitebox of 'a | Blackbox of 'a

let boxgen() = 
    gen {   let! a = arbitrary
            return! elements [ Whitebox a; Blackbox a] }

type MyGenerators =
    static member Tree() =
        {new Arbitrary<Tree>() with
            override x.Arbitrary = tree
            override x.CoArbitrary t = cotree t }
    static member Box() = 
        {new Arbitrary<Box<'a>>() with
            override x.Arbitrary = boxgen() }

registerGenerators<MyGenerators>()

let prop_RevRevTree (xs:list<Tree>) = List.rev(List.rev xs) = xs
quickCheck prop_RevRevTree

let prop_RevRevBox (xs:list<Box<int>>) = List.rev(List.rev xs) = xs
quickCheck prop_RevRevBox

//----------Tips and tricks-------
//Testing functions
let prop_Assoc (x:Tree) (f:Tree->float,g:float->char,h:char->int) = ((f >> g) >> h) x = (f >> (g >> h)) x
quickCheck prop_Assoc

//---GenReflect tests---
//a record type containing an array type
type List<'a> = {list : 'a[]}

//a recursive union type containing a record type
type Tree<'a> = 
    | Leaf of string
    | Branch of List<Tree<'a>>

let rec prop_xmlSafeTree (x : Tree<string>) =
    match x with
    | Leaf x -> not (x.StartsWith " " && x.EndsWith " ")
    | Branch xs -> Array.for_all prop_xmlSafeTree xs.list

let prop_Product (x:int,y:int) = (x > 0 && y > 0) ==> (x*y > 0)

let RevString (x : string) =
    let cs = x.ToCharArray()
    Array.Reverse cs
    new String(cs)

let prop_revstr x = RevString (RevString x) = x

let private idempotent f x = let y = f x in f y = y
quickCheck (idempotent (fun (x : string) -> x.ToUpper()))


//-------------examples from QuickCheck paper-------------
let prop_RevUnit (x:char) = List.rev [x] = [x]

let prop_RevApp (x:string) xs = 
    List.rev (x::xs) = List.rev xs @ [x] 
        |> trivial (xs = [])
        |> trivial (xs.Length = 1)

let prop_MaxLe (x:float) y = (x <= y) ==> (lazy (max  x y = y))

//----------various examples-------------------------------

//convoluted, absurd property, but shows the power of the combinators: it's no problem to return
//functions that return properties.
quickCheck (fun b y (x:char,z) -> if b then (fun q -> y+1 = z + int q) else (fun q -> q =10.0)) 

//arrays
let prop_RevRevArr (xs:int[]) = Array.rev(Array.rev xs) = xs
quickCheck prop_RevRevArr

let prop_RevRevArr2 (xs:int[][]) = xs.Rank = 1
quickCheck prop_RevRevArr2

quickCheck (fun (arr:int[]) -> Array.rev arr = arr)

type ARecord = { XPos : int; YPos : int; Name: string }

quickCheck (fun (record:ARecord) -> (record.XPos > 0 && record.YPos > 0) ==> lazy (record.XPos * record.YPos > 0))

quickCheck (fun (a:int,b,c,d:int,e,f) (g,h,i) -> a > b && b > c && d > e && f > g && e > f && h > i && a > i)

type ADisc = 
    | First of int 
    | Second of char
    | Third of ADisc
    | Fourth of ADisc[]
    
quickCheck (fun (d:ADisc) -> match d with First i -> i = 2 | Second c -> true | Third _ -> true)

type Properties =
    static member Test1 (b,(b2:bool)) = (b = b2)
    static member Test2 i = (i < 100)
    static member Test3 (i,j) = (i < 10 && j < 5.1)
    //static member Test4 l =  List.rev l = l //generic args no longer work in quickCheckAll
    static member Test5 (l:list<float>) = List.rev l = l
    //this property is falsifiable: sometimes the generator for float generates nan; and nan <> nan
    //so when checking the reverse of the reverse list's equality with the original list, the check fails. 
    static member Test6 (l:list<list<int*int> * float>) = ((l |> List.rev |> List.rev) = l) |> trivial (List.length l = 0)
    static member Test7 (a:int*bool,b:float*int) = (fst a = snd b)
    static member Test8 (l:list<obj>) = ( List.rev l = l)
    static member Test9 (s:string) = ( new String(s.ToCharArray()) = s )
    static member Test10 i = (i = 'r')
    static member NoTest i = "30"

checkAll quick (typeof<Properties>)

Console.WriteLine("----------Check all toplevel properties----------------");
type Marker = member x.Null = ()
overwriteGeneratorsByType (typeof<Marker>.DeclaringType)
quickCheckAll (typeof<Marker>.DeclaringType)

Console.ReadKey() |> ignore
