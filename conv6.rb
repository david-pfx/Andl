# script to convert andl code to syntax 6

$<.each do |line|
  line.gsub!(/recurse/,".while")
  line.gsub!(/^db/,"var")
  line.gsub!(/\]/,"")
  if line =~ /\[(.*)(\]|$)/ then
    x = $1
    x.gsub!(/\?\(/, '.where(')
    x.gsub!(/$\(/, '.order(')
    x.gsub!(/\{/, '.{')
    line.gsub!(/\[(.*)(\]|$)/, x)
  end
  puts line
end