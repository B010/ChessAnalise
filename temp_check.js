const fs=require('fs');
const s=fs.readFileSync('D:/projetos/ChessAnalise/Front/chess/src/components/Chess/ChessBoard.jsx','utf8');
function findUnmatched(o,c){let d=0; for(let i=0;i<s.length;i++){if(s[i]===o) d++; if(s[i]===c) d--; if(d<0) return {pos:i,depth:d};} return {pos:-1,depth:d};}
console.log('curly', findUnmatched('{','}'));
console.log('paren', findUnmatched('(',')'));
console.log('bracket', findUnmatched('[',']'));
