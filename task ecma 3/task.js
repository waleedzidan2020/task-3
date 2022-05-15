fruits = ["apple", "dd", "banana", "orange", "saleed"] 
// let w=fruits.every(function(i){

//     return typeof(i)=='string'
// })
// console.log(w)


// fruits = ["apple", 23, "banana", "orange", "waleed"] 

// console.log(w)
// for(let i of fruits){
// let s= i.startsWith("a")
// console.log(s)
// }


// let arr=fruits.filter(function(i){

//          return (i.startsWith("b") || i.startsWith("s"))
//      })
//      console.log(arr)

// let arr2 =fruits.map(function(i){

//     return "i like "+ i
// })
// console.log(arr2)


// arr2.forEach(element => {
//     console.log(element)
// });



// let s = new Promise(function(resolve,reject){

 
//   if(true){

//     let arr = [10,20,30,40,50,60,70]
//     resolve(arr)

//   }else{

//     reject("error")
//   }
// })

// s.then (function(data){

// console.log(data)
// }).catch(function(data){console.log(data)})
// s.catch(function(data){
//   console.log(data)
// })









async function display(){

    let data =await fetch("https://60523dc8fb49dc00175b7d04.mockapi.io/api/v1/products")
    let s = await data.json()
   for(let i in s ){

     console.log(s[i].name,s[i].price ,name,s[i].image , name,s[i].id )
   
    
   }

}

display()