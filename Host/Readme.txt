Andl is A New Database Language. See http://andl.org.

Host is an Andl sample implementing a web API server. 

Host is a web server built using Windows Communication Foundation. It accepts 
requests that conform to REST conventions, optionally with optional query arguments
and JSON body. It translates requests into Andl function calls as follows.

Assuming default database db, the parts endpoint:

HTTP GET /api/db/parts => get_parts()
HTTP GET /api/db/parts/id => get_parts_id(id)
HTTP GET /api/db/parts?name=xx => get_parts_q({{ Key:='name','Value='xx' }})
HTTP PUT /api/db/parts => put_parts(body)
HTTP POST /api/db/parts => add_parts(body)
HTTP DELETE /api/db/parts/id => delete_parts_id(id)

The sample provides a simple server based on the supplier/parts sample data, and
a range of sample requests. Due to the construction of WCF, both the server and client
are in the same program.