/**
 * @name Intentional CodeQL failure-drill finding
 * @description Finds the one eval call planted in this isolated fixture.
 * @kind problem
 * @problem.severity error
 * @precision very-high
 * @id js/vni-intentional-codeql-fixture
 */

import javascript

from CallExpr call
where call.getCalleeName() = "eval"
select call, "Intentional CodeQL failure-drill finding."
