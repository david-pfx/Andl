create temporary table i ( --
         i integer primary key, -- sudoku cell (1..81)
         name char, -- A1..H8
         word0, -- only bit i is set, i = 1..54
         word1, -- only bit i-54 is set, i = 55..81
         peers0, peers1) -- bitmap of neighbour cells
;
insert into i
select  i,
         char(65+y)||(x+1) as name,
         case when iword=0 then 1<<ibit else 0 end AS word0,
         case when iword=1 then 1<<ibit else 0 end AS word1,
         --peers0
             --horizontal
             CASE WHEN iword=0
             THEN (512-1)<<(y*9)
             ELSE 0
             END |
             --vertical
             ((1+512*(1+512*(1+512*(1+512*(1+512)))))<<x) |
             --box
             CASE WHEN iword=0
             THEN ((8-1)*(1+512*(1+512)))<<(y/3*3*9+x/3*3)
             ELSE 0
             END
             AS peers0,
         --peers1
             --horizontal
             CASE WHEN iword=1
             THEN (512-1)<<((y%6)*9)
             ELSE 0
             END |
             --vertical
             ((1 + 512 * (1 + 512)) << x) |
             --box
             CASE WHEN iword=1
             THEN ((8-1)*(1+512*(1+512)))<<((y%6)/3*3*9+x/3*3)
             ELSE 0
             END
             AS peers1
from    (
     with xy AS (SELECT 0 AS xy UNION ALL SELECT xy+1 FROM xy WHERE xy  
< 80)
     select  xy+1 AS i,
             xy%9 AS x,
             xy/9 AS y,
             xy/54 AS iword,
             xy%54 AS ibit
     from    xy
         )
;

.timer on
with z AS (
     SELECT 1 AS z UNION ALL SELECT z+1 FROM z WHERE z < 9
         )
,
input as (
     select  input,
             sud,
             ifnull (sum (word0), 0) as w0,
             ifnull (sum (word1), 0) as w1,
             ifnull (sum (case when z=1 then word0 end), 0) as w10,
             ifnull (sum (case when z=1 then word1 end), 0) as w11,
             ifnull (sum (case when z=2 then word0 end), 0) as w20,
             ifnull (sum (case when z=2 then word1 end), 0) as w21,
             ifnull (sum (case when z=3 then word0 end), 0) as w30,
             ifnull (sum (case when z=3 then word1 end), 0) as w31,
             ifnull (sum (case when z=4 then word0 end), 0) as w40,
             ifnull (sum (case when z=4 then word1 end), 0) as w41,
             ifnull (sum (case when z=5 then word0 end), 0) as w50,
             ifnull (sum (case when z=5 then word1 end), 0) as w51,
             ifnull (sum (case when z=6 then word0 end), 0) as w60,
             ifnull (sum (case when z=6 then word1 end), 0) as w61,
             ifnull (sum (case when z=7 then word0 end), 0) as w70,
             ifnull (sum (case when z=7 then word1 end), 0) as w71,
             ifnull (sum (case when z=8 then word0 end), 0) as w80,
             ifnull (sum (case when z=8 then word1 end), 0) as w81,
             ifnull (sum (case when z=9 then word0 end), 0) as w90,
             ifnull (sum (case when z=9 then word1 end), 0) as w91,
             count (*) as fixed,
             ifnull (sum (z=1), 0) as f1,
             ifnull (sum (z=2), 0) as f2,
             ifnull (sum (z=3), 0) as f3,
             ifnull (sum (z=4), 0) as f4,
             ifnull (sum (z=5), 0) as f5,
             ifnull (sum (z=6), 0) as f6,
             ifnull (sum (z=7), 0) as f7,
             ifnull (sum (z=8), 0) as f8,
             ifnull (sum (z=9), 0) as f9
     from    (
         select 'easy1' as input,
'53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79'
                     as sud
         union all
         select 'sqlite1',
'1....7.9..3..2...8..96..5....53..9...1..8...26....4...3......1..4......7..7...3..'
         union all
         select 'hard1',
'.....6....59.....82....8....45........3........6..3.54...325..6..................'
         union all
         select 'hola1',
'4.2....3.1..6.5.299.....1......42....8.9.1.5....85......3.....861.5.4..2.4....5.7'
         union all
         select 'hardest1',
'8..........36......7..9.2...5...7.......457.....1...3...1....68..85...1..9....4..'
         union all
         select 'eastermonster1',
'1.......2.9.4...5...6...7...5.9.3.......7.......85..4.7.....6...3...9.8...2.....1'
             )
     join    i
     join    z on z = cast (substr (sud, i.i, 1) as int)
     where   input='sqlite1'
         )
,
sudoku as (
     select  '' as text,
             fixed,
             f1,f2,f3,f4,f5,f6,f7,f8,f9,
             0 as zfixed,
             0 as i0,
             w0, w1,
             w10, w11,
             w20, w21,
             w30, w31,
             w40, w41,
             w50, w51,
             w60, w61,
             w70, w71,
             w80, w81,
             w90, w91
     from    input
     union all
     select  --text
                 (fixed+1)||','||
                 name||','||
                 (
                         (w10&peers0 or w11&peers1) +
                         (w20&peers0 or w21&peers1) +
                         (w30&peers0 or w31&peers1) +
                         (w40&peers0 or w41&peers1) +
                         (w50&peers0 or w51&peers1) +
                         (w60&peers0 or w61&peers1) +
                         (w70&peers0 or w71&peers1) +
                         (w80&peers0 or w81&peers1) +
                         (w90&peers0 or w91&peers1)
                         ) ||','||
                 z||','||
                 '',
             fixed+1,
             f1+(z=1),
             f2+(z=2),
             f3+(z=3),
             f4+(z=4),
             f5+(z=5),
             f6+(z=6),
             f7+(z=7),
             f8+(z=8),
             f9+(z=9),
             --zfixed
                 case z
                 when 1 then f1
                 when 2 then f2
                 when 3 then f3
                 when 4 then f4
                 when 5 then f5
                 when 6 then f6
                 when 7 then f7
                 when 8 then f8
                 when 9 then f9
                 end,
             i.i,
             w0+word0,
             w1+word1,
             case when z=1 then  w10+word0 else w10 end,
             case when z=1 then  w11+word1 else w11 end,
             case when z=2 then  w20+word0 else w20 end,
             case when z=2 then  w21+word1 else w21 end,
             case when z=3 then  w30+word0 else w30 end,
             case when z=3 then  w31+word1 else w31 end,
             case when z=4 then  w40+word0 else w40 end,
             case when z=4 then  w41+word1 else w41 end,
             case when z=5 then  w50+word0 else w50 end,
             case when z=5 then  w51+word1 else w51 end,
             case when z=6 then  w60+word0 else w60 end,
             case when z=6 then  w61+word1 else w61 end,
             case when z=7 then  w70+word0 else w70 end,
             case when z=7 then  w71+word1 else w71 end,
             case when z=8 then  w80+word0 else w80 end,
             case when z=8 then  w81+word1 else w81 end,
             case when z=9 then  w90+word0 else w90 end,
             case when z=9 then  w91+word1 else w91 end
     from    sudoku
     join    i
     on      i = ifnull (
                         (
                     select  i
                     from    i
                     where   i > i0
                     and     not (word0&w0 or word1&w1)
                     and     (
                                 (w10&peers0 or w11&peers1) +
                                 (w20&peers0 or w21&peers1) +
                                 (w30&peers0 or w31&peers1) +
                                 (w40&peers0 or w41&peers1) +
                                 (w50&peers0 or w51&peers1) +
                                 (w60&peers0 or w61&peers1) +
                                 (w70&peers0 or w71&peers1) +
                                 (w80&peers0 or w81&peers1) +
                                 (w90&peers0 or w91&peers1)
                              ) = 8
                     limit 1
                         )
                 ,

                 (
             select  i
             from    (
                 select  i,
                         max (
                             (w10&peers0 or w11&peers1) +
                             (w20&peers0 or w21&peers1) +
                             (w30&peers0 or w31&peers1) +
                             (w40&peers0 or w41&peers1) +
                             (w50&peers0 or w51&peers1) +
                             (w60&peers0 or w61&peers1) +
                             (w70&peers0 or w71&peers1) +
                             (w80&peers0 or w81&peers1) +
                             (w90&peers0 or w91&peers1)
                             ) as maxfixed
                 from    i
                 where   not (word0&w0 or word1&w1)
                     )
             where     maxfixed is not null -- break sqlite optimizer
                 ))
     join    z
     on
                 case z
                 when 1 then not (w10&peers0 or w11&peers1)
                 when 2 then not (w20&peers0 or w21&peers1)
                 when 3 then not (w30&peers0 or w31&peers1)
                 when 4 then not (w40&peers0 or w41&peers1)
                 when 5 then not (w50&peers0 or w51&peers1)
                 when 6 then not (w60&peers0 or w61&peers1)
                 when 7 then not (w70&peers0 or w71&peers1)
                 when 8 then not (w80&peers0 or w81&peers1)
                 when 9 then not (w90&peers0 or w91&peers1)
                 end
     order by fixed desc, --depth first
             zfixed desc --most used digit first
         )
,
output as (
     select  *
     from    (
         select  1 as i,
                 w10, w11,
                 w20, w21,
                 w30, w31,
                 w40, w41,
                 w50, w51,
                 w60, w61,
                 w70, w71,
                 w80, w81,
                 w90, w91,
                 '' as sud
         from    sudoku
         where   fixed = 81 limit 1
             )
     union all
     select  nullif (output.i + 1, 82),
             w10, w11,
             w20, w21,
             w30, w31,
             w40, w41,
             w50, w51,
             w60, w61,
             w70, w71,
             w80, w81,
             w90, w91,
             sud || replace (cast (
                 case 1
                 when w10&word0 OR w11&word1 then 1
                 when w20&word0 OR w21&word1 then 2
                 when w30&word0 OR w31&word1 then 3
                 when w40&word0 OR w41&word1 then 4
                 when w50&word0 OR w51&word1 then 5
                 when w60&word0 OR w61&word1 then 6
                 when w70&word0 OR w71&word1 then 7
                 when w80&word0 OR w81&word1 then 8
                 when w90&word0 OR w91&word1 then 9
                 else 0
                 end
                 as char), '0', '.')
     from    output
     join    i on i.i = output.i
         )
--select  * from input
--select text from sudoku-- where fixed = 81 limit 1
select  substr (sud, z*9-8, 3) as s1, substr (sud, z*9-5, 3) as s2,  
substr (sud, z*9-2, 3) as s3 from    ( select  1 as io, sud from input  
union all select  2 as io, sud from output where i is null) sud  
join    z order by io, z
;