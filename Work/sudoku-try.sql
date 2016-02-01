WITH RECURSIVE
  input(sud) AS (
    VALUES
    --('53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79'),
    --('1....7.9..3..2...8..96..5....53..9...1..8...26....4...3......1..4......7..7...3..'),
      ('1.......2.9.4...5...6...7...5.9.3.......7.......85..4.7.....6...3...9.8...2.....1')
  ),
  digits(z, lp) AS (
    VALUES('1', 1)
    UNION ALL SELECT
    CAST(lp+1 AS TEXT), lp+1 FROM digits WHERE lp<9
  ),
  x(s, ind) AS (
    SELECT sud, instr(sud, '.') FROM input
    UNION ALL
    SELECT
      substr(s, 1, ind-1) || z || substr(s, ind+1),
      instr( substr(s, 1, ind-1) || z || substr(s, ind+1), '.' )
     FROM x, digits AS z
    WHERE ind>0
      AND NOT EXISTS (
            SELECT 1
              FROM digits
             WHERE z.z = substr(s, ((ind-1)/9)*9 + lp, 1)
                OR z.z = substr(s, ((ind-1)%9) + (lp-1)*9 + 1, 1)
                OR z.z = substr(s, (((ind-1)/3) % 3) * 3
                        + ((ind-1)/27) * 27 + lp
                        + ((lp-1) / 3) * 6, 1)
         )
  )
select * from x
--select count(*) from x
--SELECT s FROM x WHERE ind=0
;
