// addresses.thrift
// Thrift IDL file generated by Andl -- do not edit

namespace * addresses

typedef string text
typedef double number
typedef i64 time

struct addresses {
  1: number address_num;
  2: text first_name;
  3: text last_name;
  4: text nickname;
  5: text email;
  6: text screen_name;
  7: text work_phone;
  8: text home_phone;
  9: text fax;
  10: text pager;
  11: text street;
  12: text city;
  13: text state;
  14: text zip;
  15: text country;
  16: text company;
  17: text work_street;
  18: text work_city;
  19: text work_state;
  20: text work_zip;
  21: text work_country;
  22: text webpage;
  23: text notes;
  24: time birthday;
  25: text cell_phone;
}
service addressesService {
  void addresses_add(
    1: list<addresses> a;
  );
}